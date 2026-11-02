package com.tyrelfrisby.aeroplayground;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.util.Log;

import java.io.File;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.concurrent.ArrayBlockingQueue;

/**
 * The Quest's "what I see" recorder, the Android half (C#: Bridge/QuestRecorder.cs). Unity hands it I420 frames
 * the GPU has already packed (Shaders/QuestRecordPack.shader) and 16-bit PCM of the game's own mix; this encodes
 * H.264 + AAC with MediaCodec and writes an .mp4 with MediaMuxer.
 *
 * Threads, so Unity's main thread only ever does a memcpy:
 *   - the caller (Unity main thread): video() / audio() copy the data out of Unity's buffer and post it;
 *   - "QuestRecorder-in": converts into the encoders' input images (any stride / pixel stride) and queues them;
 *   - "QuestRecorder-out": drains both encoders into the muxer, and finalises the file on stop.
 * A frame that finds no free buffer or no free encoder input is dropped and counted, never waited for.
 *
 * The file is only playable once stop() has run (MediaMuxer writes the index last), so the C# side calls it on
 * pause and quit and waits for it.
 */
public final class QuestRecorder {
    private static final String TAG = "QuestRecorder";
    private static final Object LOCK = new Object();
    private static Session session;
    private static volatile String message = "";
    private static volatile int messageSeq;

    private QuestRecorder() { }

    // ---- the API Unity calls -----------------------------------------------------------------------------

    /** Start a recording to {@code path}. False (and a message) if the encoders could not be made. */
    public static boolean start(String path, int width, int height, int fps, int videoBitrate,
                                int sampleRate, int channels, int audioBitrate) {
        synchronized (LOCK) {
            if (session != null) { say(session.stopping ? "Recording: still saving the last one" : "Recording: already running"); return false; }
            try {
                File dir = new File(path).getParentFile();
                if (dir != null && !dir.isDirectory() && !dir.mkdirs()) throw new IllegalStateException("cannot create " + dir);
                session = new Session(path, width, height, fps, videoBitrate, sampleRate, channels, audioBitrate);
                session.begin();
                Log.i(TAG, "recording " + width + "x" + height + " @" + fps + " to " + path
                        + (session.aenc != null ? " with audio" : " (no audio)"));
                return true;
            } catch (Throwable t) {
                Log.e(TAG, "start failed", t);
                if (session != null) session.abandon();
                session = null;
                say("Recording failed: " + t.getMessage());
                return false;
            }
        }
    }

    /** One packed I420 frame ({@code bytes} long). Copies it and returns at once; false = dropped. */
    public static boolean video(ByteBuffer i420, int bytes, long ptsUs) {
        Session s = session;
        return s != null && s.offerVideo(i420, bytes, ptsUs);
    }

    /** {@code bytes} of interleaved 16-bit PCM whose first sample is at {@code ptsUs}. */
    public static void audio(ByteBuffer pcm16, int bytes, long ptsUs) {
        Session s = session;
        if (s != null) s.offerAudio(pcm16, bytes, ptsUs);
    }

    /** Stop and finalise. With {@code waitMs > 0}, block until the file is written (or the time is up). */
    public static void stop(int waitMs) {
        Session s;
        synchronized (LOCK) { s = session; }
        if (s == null) return;
        s.requestStop();
        if (waitMs > 0) s.await(waitMs);
    }

    public static boolean isRecording() { Session s = session; return s != null && !s.stopping; }
    public static boolean isFinishing() { Session s = session; return s != null && s.stopping; }
    public static int droppedFrames() { Session s = session; return s != null ? s.dropped : 0; }
    public static int messageSeq() { return messageSeq; }
    public static String message() { return message; }

    private static void say(String m) {
        message = m;
        messageSeq++;
        Log.i(TAG, m);
    }

    // ---- one recording -----------------------------------------------------------------------------------

    private static final class Session {
        final String path;
        final int w, h, fps, frameBytes, sampleRate, channels;
        final MediaCodec venc;
        MediaCodec aenc;
        final MediaMuxer muxer;
        final HandlerThread inThread = new HandlerThread("QuestRecorder-in");
        Handler in;
        Thread outThread;
        final ArrayBlockingQueue<byte[]> free = new ArrayBlockingQueue<>(3);
        volatile boolean stopping;
        volatile int dropped;
        long lastVideoPts = -1, lastAudioEnd = -1;
        int vTrack = -1, aTrack = -1;
        boolean muxing, audioGivenUp, vEos, aEos;
        long videoFormatAt;
        long firstPts = -1, lastPts;
        int samples;
        long stopAt;
        final ArrayList<Pending> pending = new ArrayList<>();
        final Object done = new Object();
        boolean finished;

        Session(String path, int w, int h, int fps, int videoBitrate, int sampleRate, int channels, int audioBitrate) throws Exception {
            this.path = path; this.w = w; this.h = h; this.fps = fps;
            this.frameBytes = w * h * 3 / 2;
            this.sampleRate = sampleRate; this.channels = channels;

            MediaFormat vf = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h);
            vf.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
            vf.setInteger(MediaFormat.KEY_BIT_RATE, videoBitrate);
            vf.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
            vf.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
            venc = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
            venc.configure(vf, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);

            if (sampleRate > 0 && channels > 0) {
                try {
                    MediaFormat af = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, sampleRate, channels);
                    af.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
                    af.setInteger(MediaFormat.KEY_BIT_RATE, audioBitrate);
                    af.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 16384);
                    aenc = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC);
                    aenc.configure(af, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                } catch (Throwable t) {
                    Log.w(TAG, "no audio encoder, recording video only", t);
                    if (aenc != null) { try { aenc.release(); } catch (Throwable ignored) { } }
                    aenc = null;
                }
            }
            muxer = new MediaMuxer(path, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            for (int i = 0; i < 3; i++) free.add(new byte[frameBytes]);
        }

        void begin() {
            venc.start();
            if (aenc != null) aenc.start();
            inThread.start();
            in = new Handler(inThread.getLooper());
            outThread = new Thread(this::drainLoop, "QuestRecorder-out");
            outThread.start();
        }

        // -- input (caller thread → in thread) --

        boolean offerVideo(ByteBuffer src, int bytes, long ptsUs) {
            if (stopping || bytes != frameBytes) return false;
            byte[] b = free.poll();
            if (b == null) { dropped++; return false; }
            src.clear();
            src.get(b, 0, bytes);
            in.post(() -> feedVideo(b, ptsUs));
            return true;
        }

        void offerAudio(ByteBuffer src, int bytes, long ptsUs) {
            if (stopping || aenc == null || audioGivenUp || bytes <= 0) return;
            byte[] b = new byte[bytes];
            src.clear();
            src.get(b, 0, bytes);
            in.post(() -> feedAudio(b, ptsUs));
        }

        private void feedVideo(byte[] b, long ptsUs) {
            try {
                if (stopping) return;
                int idx = venc.dequeueInputBuffer(20_000);
                if (idx < 0) { dropped++; return; }
                if (ptsUs <= lastVideoPts) ptsUs = lastVideoPts + 1;   // the muxer wants rising timestamps
                lastVideoPts = ptsUs;
                if (firstPts < 0) firstPts = ptsUs;
                lastPts = ptsUs;
                Image img = venc.getInputImage(idx);
                int y = w * h, c = y / 4;
                if (img != null) {
                    Image.Plane[] p = img.getPlanes();
                    copyPlane(b, 0, w, h, p[0]);
                    copyPlane(b, y, w / 2, h / 2, p[1]);
                    copyPlane(b, y + c, w / 2, h / 2, p[2]);
                } else {
                    ByteBuffer ib = venc.getInputBuffer(idx);   // no Image: hope for planar
                    ib.clear();
                    ib.put(b, 0, frameBytes);
                }
                venc.queueInputBuffer(idx, 0, frameBytes, ptsUs, 0);
            } catch (Throwable t) {
                Log.e(TAG, "video input", t);
            } finally {
                free.offer(b);
            }
        }

        /** Tight rows in, whatever row / pixel stride the encoder chose out. */
        private static void copyPlane(byte[] src, int off, int pw, int ph, Image.Plane plane) {
            ByteBuffer dst = plane.getBuffer();
            int rs = plane.getRowStride(), ps = plane.getPixelStride();
            if (ps == 1) {
                if (rs == pw) { dst.position(0); dst.put(src, off, pw * ph); return; }
                for (int r = 0; r < ph; r++) { dst.position(r * rs); dst.put(src, off + r * pw, pw); }
                return;
            }
            for (int r = 0; r < ph; r++) {
                int base = r * rs, s = off + r * pw;
                for (int x = 0; x < pw; x++) dst.put(base + x * ps, src[s + x]);
            }
        }

        private void feedAudio(byte[] b, long ptsUs) {
            try {
                if (stopping || audioGivenUp) return;
                int frameSize = 2 * channels;
                if (ptsUs < lastAudioEnd) ptsUs = lastAudioEnd;
                int off = 0;
                while (off < b.length) {
                    int idx = aenc.dequeueInputBuffer(20_000);
                    if (idx < 0) return;   // the encoder is behind: this chunk is lost, the timestamps stay right
                    ByteBuffer ib = aenc.getInputBuffer(idx);
                    ib.clear();
                    int n = Math.min(b.length - off, ib.remaining() / frameSize * frameSize);
                    if (n <= 0) { aenc.queueInputBuffer(idx, 0, 0, ptsUs, 0); return; }
                    ib.put(b, off, n);
                    long pts = ptsUs + (long) (off / frameSize) * 1_000_000L / sampleRate;
                    aenc.queueInputBuffer(idx, 0, n, pts, 0);
                    off += n;
                    samples += n / frameSize;
                }
                lastAudioEnd = ptsUs + (long) (b.length / frameSize) * 1_000_000L / sampleRate;
            } catch (Throwable t) {
                Log.e(TAG, "audio input", t);
            }
        }

        // -- stopping --

        void requestStop() {
            synchronized (this) {
                if (stopping) return;
                stopping = true;
                stopAt = SystemClock.elapsedRealtime();
            }
            in.post(() -> {
                endOfStream(venc, lastVideoPts + 1);
                if (aenc != null) endOfStream(aenc, Math.max(lastAudioEnd, 0));
            });
        }

        private static void endOfStream(MediaCodec c, long pts) {
            try {
                int idx = c.dequeueInputBuffer(500_000);
                if (idx >= 0) c.queueInputBuffer(idx, 0, 0, Math.max(pts, 0), MediaCodec.BUFFER_FLAG_END_OF_STREAM);
            } catch (Throwable t) {
                Log.w(TAG, "end of stream", t);
            }
        }

        void await(int ms) {
            long until = SystemClock.elapsedRealtime() + ms;
            synchronized (done) {
                while (!finished) {
                    long left = until - SystemClock.elapsedRealtime();
                    if (left <= 0) { Log.w(TAG, "stop: still finishing after " + ms + " ms"); return; }
                    try { done.wait(left); } catch (InterruptedException e) { return; }
                }
            }
        }

        /** start() failed half-way: release what exists, leave no file. */
        void abandon() {
            try { venc.release(); } catch (Throwable ignored) { }
            if (aenc != null) { try { aenc.release(); } catch (Throwable ignored) { } }
            try { muxer.release(); } catch (Throwable ignored) { }
            inThread.quitSafely();
            new File(path).delete();
        }

        // -- output (out thread) --

        private final MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();

        private void drainLoop() {
            String outcome;
            try {
                while (true) {
                    drain(venc, true, 10_000);
                    if (aenc != null) drain(aenc, false, 0);
                    long now = SystemClock.elapsedRealtime();
                    // No audio format two seconds after the video's: record the video alone rather than nothing.
                    if (!muxing && vTrack >= 0 && aenc != null && aTrack < 0 && !audioGivenUp && now - videoFormatAt > 2000) {
                        Log.w(TAG, "no audio arrived: recording video only");
                        audioGivenUp = true;
                        startMuxer();
                    }
                    boolean audioDone = aenc == null || aEos || audioGivenUp;
                    if (vEos && audioDone) break;
                    if (stopping && now - stopAt > 4000) { Log.w(TAG, "encoders did not finish in 4 s: closing the file anyway"); break; }
                }
                outcome = finish();
            } catch (Throwable t) {
                Log.e(TAG, "recording failed", t);
                outcome = "Recording failed: " + t.getMessage();
                try { muxer.release(); } catch (Throwable ignored) { }
                new File(path).delete();
            }
            try { venc.stop(); } catch (Throwable ignored) { }
            try { venc.release(); } catch (Throwable ignored) { }
            if (aenc != null) {
                try { aenc.stop(); } catch (Throwable ignored) { }
                try { aenc.release(); } catch (Throwable ignored) { }
            }
            inThread.quitSafely();
            synchronized (LOCK) { if (session == this) session = null; }
            say(outcome);
            synchronized (done) { finished = true; done.notifyAll(); }
        }

        private String finish() {
            String name = new File(path).getName();
            if (!muxing) {
                try { muxer.release(); } catch (Throwable ignored) { }
                new File(path).delete();
                return "Recording: nothing was captured";
            }
            muxer.stop();
            muxer.release();
            double sec = firstPts >= 0 ? (lastPts - firstPts) / 1e6 : 0;
            Log.i(TAG, "saved " + path + String.format(" (%.1f s, %d frames dropped, %d audio samples)", sec, dropped, samples));
            return "Saved " + name;
        }

        private void drain(MediaCodec c, boolean video, long timeoutUs) {
            while (true) {
                int out = c.dequeueOutputBuffer(info, timeoutUs);
                if (out == MediaCodec.INFO_TRY_AGAIN_LATER) return;
                if (out == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                    if (muxing) { Log.w(TAG, "format changed after the muxer started: ignored"); continue; }
                    if (video) { vTrack = muxer.addTrack(c.getOutputFormat()); videoFormatAt = SystemClock.elapsedRealtime(); }
                    else if (!audioGivenUp) aTrack = muxer.addTrack(c.getOutputFormat());
                    if (vTrack >= 0 && (aenc == null || aTrack >= 0)) startMuxer();
                    continue;
                }
                if (out < 0) continue;
                ByteBuffer ob = c.getOutputBuffer(out);
                boolean eos = (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
                if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) info.size = 0;
                int track = video ? vTrack : aTrack;
                if (info.size > 0 && ob != null && track >= 0) {
                    ob.position(info.offset);
                    ob.limit(info.offset + info.size);
                    if (muxing) muxer.writeSampleData(track, ob, info);
                    else pending.add(new Pending(track, ob, info));
                }
                c.releaseOutputBuffer(out, false);
                if (eos) { if (video) vEos = true; else aEos = true; return; }
            }
        }

        private void startMuxer() {
            if (muxing) return;
            muxer.start();
            muxing = true;
            MediaCodec.BufferInfo bi = new MediaCodec.BufferInfo();
            for (Pending p : pending) {
                bi.set(0, p.data.length, p.pts, p.flags);
                muxer.writeSampleData(p.track, ByteBuffer.wrap(p.data), bi);
            }
            pending.clear();
        }
    }

    /** Encoded output that arrived before the muxer could start (it needs every track's format first). */
    private static final class Pending {
        final int track, flags;
        final long pts;
        final byte[] data;
        Pending(int track, ByteBuffer src, MediaCodec.BufferInfo info) {
            this.track = track; this.pts = info.presentationTimeUs; this.flags = info.flags;
            data = new byte[info.size];
            src.get(data);
        }
    }
}
