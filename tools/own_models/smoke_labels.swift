// Add legible system-font labels to the offline CPU renders. No GUI or network needed.
import Foundation
import CoreGraphics
import CoreText
import ImageIO
import UniformTypeIdentifiers

let directory = URL(fileURLWithPath: CommandLine.arguments[1])
let annotations = try JSONSerialization.jsonObject(with: Data(contentsOf: directory.appendingPathComponent("annotations.json"))) as! [String: [[String: Any]]]
for (id, labels) in annotations {
    let url = directory.appendingPathComponent(id + ".png")
    let source = CGImageSourceCreateWithURL(url as CFURL, nil)!
    let image = CGImageSourceCreateImageAtIndex(source, 0, nil)!
    let ctx = CGContext(data: nil, width: image.width, height: image.height, bitsPerComponent: 8,
                        bytesPerRow: image.width * 4, space: CGColorSpaceCreateDeviceRGB(),
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
    // Reserve an opaque footer so legends remain readable over wheels, floats and wings.
    ctx.setFillColor(CGColor(gray: 0.96, alpha: 1))
    ctx.fill(CGRect(x: 0, y: 0, width: image.width, height: 100))
    for label in labels {
        let rgb = label["color"] as! [Double]
        let color = CGColor(red: rgb[0], green: rgb[1], blue: rgb[2], alpha: 1)
        let attributes: [NSAttributedString.Key: Any] = [
            NSAttributedString.Key(kCTFontAttributeName as String): CTFontCreateWithName("Helvetica" as CFString, label["size"] as! CGFloat, nil),
            NSAttributedString.Key(kCTForegroundColorAttributeName as String): color
        ]
        let value = NSAttributedString(string: label["text"] as! String, attributes: attributes)
        ctx.textPosition = CGPoint(x: label["x"] as! Double, y: Double(image.height) - (label["y"] as! Double))
        CTLineDraw(CTLineCreateWithAttributedString(value), ctx)
    }
    let dest = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(dest, ctx.makeImage()!, nil)
    if !CGImageDestinationFinalize(dest) { fatalError("could not write " + id) }
}
