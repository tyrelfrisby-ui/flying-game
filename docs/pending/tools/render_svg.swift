// Native macOS SVG-to-PNG review renderer. No package dependencies.
import AppKit
let args = CommandLine.arguments
guard args.count == 3, let image = NSImage(contentsOfFile: args[1]),
      let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
      let png = bitmap.representation(using: .png, properties: [:]) else {
    fputs("Usage: swift render_svg.swift input.svg output.png; valid SVG required\n", stderr)
    exit(1)
}
try png.write(to: URL(fileURLWithPath: args[2]))
