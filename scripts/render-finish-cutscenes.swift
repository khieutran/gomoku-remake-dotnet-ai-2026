import AppKit
import Foundation

// Four independent character cutscenes. Writes 24 fps MJPEG frames to stdout.
// Encode the stream as VP8/WebM with ffmpeg's image2pipe demuxer.

let w = 800, h = 450, fps = 24, frames = 108
let scene = CommandLine.arguments.dropFirst().first ?? ""
guard ["sword", "arrow", "fire", "electric"].contains(scene) else {
    fputs("Usage: render-finish-cutscenes [sword|arrow|fire|electric]\n", stderr)
    exit(2)
}

func rgb(_ value: UInt32, _ alpha: Double = 1) -> CGColor {
    CGColor(red: CGFloat((value >> 16) & 255)/255,
            green: CGFloat((value >> 8) & 255)/255,
            blue: CGFloat(value & 255)/255, alpha: CGFloat(alpha))
}
let ink = rgb(0x261b31), cream = rgb(0xffebc9), cobalt = rgb(0x3379bd)
let ice = rgb(0xb8e7f2), gold = rgb(0xffcf65), red = rgb(0xe45b6a)
let steel = rgb(0x91afc0), bot = rgb(0x6ac5b5)
let assetDir = "frontend/public/images/finish-cutscenes"
func loadSprite(_ name: String) -> CGImage {
    guard let image = NSImage(contentsOfFile:"\(assetDir)/\(name).png"),
          let cg = image.cgImage(forProposedRect:nil,context:nil,hints:nil) else {
        fputs("Missing sprite: \(name).png\n",stderr); exit(5)
    }
    return cg
}
let knightSprite = loadSprite("knight")
let bowSprite = loadSprite("knight-bow")
let robotSprite = loadSprite("robot")
let chickenSprite = loadSprite("chicken")
let run1Sprite = loadSprite("knight-run-1")
let run2Sprite = loadSprite("knight-run-2")
let windupSprite = loadSprite("knight-windup")
let slashSprite = loadSprite("knight-slash")
let robotAimSprite = loadSprite("robot-aim")
let knightShockSprite = loadSprite("knight-shock")
let bowReleaseSprite = loadSprite("knight-bow-release")
let robotFireSprite = loadSprite("robot-fire")
let headCut = Int(Double(robotSprite.height)*0.43)
let robotHeadSprite = robotSprite.cropping(to:CGRect(x:0,y:0,width:robotSprite.width,height:headCut))!
let robotBodySprite = robotSprite.cropping(to:CGRect(x:0,y:headCut,width:robotSprite.width,height:robotSprite.height-headCut))!
func clamp(_ t: Double) -> Double { max(0, min(1, t)) }
func ease(_ t: Double) -> Double { let a = clamp(t); return a*a*(3-2*a) }
func fill(_ c: CGContext, _ x: Double, _ y: Double, _ width: Double, _ height: Double,
          _ shade: CGColor, stroke: CGColor? = nil, weight: Double = 2) {
    let r = CGRect(x:x, y:Double(h)-y-height, width:width, height:height)
    c.setFillColor(shade); c.fill(r)
    if let stroke { c.setStrokeColor(stroke); c.setLineWidth(weight); c.stroke(r) }
}
func oval(_ c: CGContext, _ x: Double, _ y: Double, _ rx: Double, _ ry: Double,
          _ shade: CGColor, stroke: CGColor? = nil, weight: Double = 2) {
    let r = CGRect(x:x-rx,y:Double(h)-y-ry,width:rx*2,height:ry*2)
    c.setFillColor(shade); c.fillEllipse(in:r)
    if let stroke { c.setStrokeColor(stroke); c.setLineWidth(weight); c.strokeEllipse(in:r) }
}
func line(_ c: CGContext, _ x1: Double, _ y1: Double, _ x2: Double, _ y2: Double,
          _ shade: CGColor, _ weight: Double) {
    c.setStrokeColor(shade); c.setLineWidth(weight); c.setLineCap(.round)
    c.move(to:CGPoint(x:x1,y:Double(h)-y1)); c.addLine(to:CGPoint(x:x2,y:Double(h)-y2)); c.strokePath()
}
func words(_ value: String, _ x: Double, _ y: Double, _ size: Double, _ shade: CGColor) {
    let font = NSFont.monospacedSystemFont(ofSize:CGFloat(size),weight:.heavy)
    let attrs: [NSAttributedString.Key:Any] = [.font:font,.foregroundColor:NSColor(cgColor:shade) ?? .white]
    let width = (value as NSString).size(withAttributes:attrs).width
    (value as NSString).draw(at:NSPoint(x:x-width/2,y:Double(h)-y-size*1.2),withAttributes:attrs)
}
func part(_ c: CGContext, _ x: Double, _ y: Double, _ scale: Double,
          _ a: Double, _ b: Double, _ width: Double, _ height: Double, _ shade: CGColor) {
    fill(c,x+a*scale,y+b*scale,width*scale,height*scale,shade,stroke:ink,weight:2.4*scale)
}

func sprite(_ c: CGContext, _ image: CGImage, _ x: Double, _ floorY: Double,
            _ size: Double, opacity: Double = 1, tilt: Double = 0, flipX: Bool = false) {
    c.saveGState()
    c.setAlpha(CGFloat(opacity))
    c.interpolationQuality = .none
    c.translateBy(x:x,y:Double(h)-floorY)
    c.rotate(by:CGFloat(tilt))
    if flipX { c.scaleBy(x:-1,y:1) }
    c.draw(image,in:CGRect(x:-size/2,y:0,width:size,height:size))
    c.restoreGState()
}
func fallenRobot(_ c: CGContext, _ x: Double, _ floorY: Double, _ size: Double,
                 _ progress: Double) {
    let p = ease(progress)
    let bodyHeight = size*Double(robotBodySprite.height)/Double(robotSprite.height)
    let headHeight = size-bodyHeight
    let baseY = Double(h)-floorY
    c.saveGState(); c.interpolationQuality = .none
    c.translateBy(x:x,y:baseY)
    c.rotate(by:CGFloat(-0.18*p))
    c.draw(robotBodySprite,in:CGRect(x:-size/2,y:0,width:size,height:bodyHeight))
    c.restoreGState()
    let headX = x + 85*p
    let headDrop = 105*p-24*sin(Double.pi*p)
    c.saveGState(); c.interpolationQuality = .none
    c.translateBy(x:headX,y:baseY+bodyHeight+headHeight/2-headDrop)
    c.rotate(by:CGFloat(-1.25*p))
    c.draw(robotHeadSprite,in:CGRect(x:-size/2,y:-headHeight/2,width:size,height:headHeight))
    c.restoreGState()
}
func headlessRobot(_ c: CGContext, _ x: Double, _ floorY: Double, _ size: Double,
                   _ recoil: Double) {
    let bodyHeight = size*Double(robotBodySprite.height)/Double(robotSprite.height)
    c.saveGState(); c.interpolationQuality = .none
    c.translateBy(x:x,y:Double(h)-floorY)
    c.rotate(by:CGFloat(-0.15*recoil))
    c.draw(robotBodySprite,in:CGRect(x:-size/2,y:0,width:size,height:bodyHeight))
    c.restoreGState()
}

// Pixel art character sprites generated from the supplied visual references.
func knight(_ c: CGContext, _ x: Double, _ y: Double, _ s: Double, pose: String = "idle",
            soot: Bool = false, fade: Double = 1) {
    sprite(c,pose == "bow" ? bowSprite : knightSprite,x,y+110*s,215*s,opacity:fade)
    if soot {
        for i in 0..<4 {
            oval(c,x+Double(i*23-35)*s,y-10-Double(i%2*25),10*s,9*s,rgb(0x242332,0.5*fade))
        }
    }
}
func robot(_ c: CGContext, _ x: Double, _ y: Double, _ s: Double,
           eye: CGColor = gold, broken: Bool = false, fade: Double = 1) {
    sprite(c,robotSprite,x,y+110*s,215*s,opacity:fade,tilt:broken ? -0.13 : 0)
    if broken && fade > 0 {
        spark(c,x,y-10,36,red,8)
    }
}
func chicken(_ c: CGContext, _ x: Double, _ y: Double, _ s: Double) {
    sprite(c,chickenSprite,x,y+100*s,150*s)
}
func spark(_ c: CGContext, _ x: Double, _ y: Double, _ radius: Double,
           _ shade: CGColor, _ count: Int = 12) {
    for i in 0..<count {
        let a = Double(i)*2*Double.pi/Double(count)
        line(c,x+cos(a)*radius*0.45,y+sin(a)*radius*0.45,
             x+cos(a)*radius,y+sin(a)*radius,shade,3)
    }
}
func burst(_ c: CGContext, _ x: Double, _ y: Double, _ progress: Double,
           _ shade: CGColor) {
    let p = ease(progress)
    for i in 0..<20 {
        let a = Double(i)*2*Double.pi/20
        let r = 20+130*p+Double(i%3)*9
        let px = x+cos(a)*r, py = y+sin(a)*r
        oval(c,px,py,Double(4+i%4)*(1-p)+2,Double(4+i%4)*(1-p)+2,
             i%2 == 0 ? shade : gold)
    }
}
func flameBand(_ c: CGContext, _ fromX: Double, _ toX: Double, _ centerY: Double,
               _ halfHeight: Double, _ t: Double, _ shade: CGColor) {
    guard toX > fromX+12 else { return }
    let steps = 18
    let path = CGMutablePath()
    for i in 0...steps {
        let p = Double(i)/Double(steps)
        let x = fromX+(toX-fromX)*p
        let taper = max(0.2,1-0.78*p)
        let jag = sin(Double(i)*2.8+t*22)*halfHeight*0.3
        let y = centerY-halfHeight*taper-jag
        if i == 0 { path.move(to:CGPoint(x:x,y:Double(h)-y)) }
        else { path.addLine(to:CGPoint(x:x,y:Double(h)-y)) }
    }
    for i in (0...steps).reversed() {
        let p = Double(i)/Double(steps)
        let x = fromX+(toX-fromX)*p
        let taper = max(0.2,1-0.78*p)
        let jag = sin(Double(i)*2.2+t*19+1.4)*halfHeight*0.27
        let y = centerY+halfHeight*taper+jag
        path.addLine(to:CGPoint(x:x,y:Double(h)-y))
    }
    path.closeSubpath()
    c.addPath(path); c.setFillColor(shade); c.fillPath()
}
func stage(_ c: CGContext, _ t: Double) {
    fill(c,0,0,800,450,rgb(0x1c1830))
    fill(c,0,0,800,260,rgb(0x263651))
    for i in 0..<20 {
        let x = Double((i*127+53)%800)
        let y = Double((i*71+29)%220)
        oval(c,x,y,Double(i%3+1),Double(i%3+1),rgb(0xb8e7f2,0.35))
    }
    oval(c,400,285,280,160,rgb(0x6b7799,0.18))
    fill(c,0,350,800,100,rgb(0x312941))
    fill(c,0,348,800,7,rgb(0xb8837d))
    for i in 0..<13 { fill(c,Double(i*72)-20,390,45,6,rgb(0x52415a)) }
    if t > 3.15 {
        fill(c,0,0,800,450,rgb(0x130f22,0.45*ease((t-3.15)/0.25)))
    }
}
func endCard(_ c: CGContext, _ t: Double, human: Bool) {
    let p = ease((t-3.35)/0.3)
    if p == 0 { return }
    c.saveGState(); c.setAlpha(CGFloat(p))
    fill(c,192,161,416,120,ink,stroke:human ? gold : red,weight:5)
    words(human ? "NGƯỜI THẮNG" : "BOT THẮNG",400,188,42,human ? gold : cream)
    words(human ? "HUMAN VICTORY" : "ROBOT VICTORY",400,246,18,ice)
    c.restoreGState()
}
func swordScene(_ c: CGContext, _ t: Double) {
    let dash = ease((t-0.55)/0.9)
    let hx = 155+315*dash
    let knightFrame: CGImage
    if t < 0.55 { knightFrame = knightSprite }
    else if t < 1.45 { knightFrame = Int((t-0.55)/0.12)%2 == 0 ? run1Sprite : run2Sprite }
    else if t < 1.82 { knightFrame = windupSprite }
    else { knightFrame = slashSprite }
    let bob = t > 0.55 && t < 1.45 ? 5*abs(sin((t-0.55)*Double.pi/0.12)) : 0
    if t < 1.95 { sprite(c,robotSprite,610,350,262) }
    else { fallenRobot(c,610,350,262,(t-1.95)/0.9) }
    if t > 0.55 && t < 1.45 {
        for i in 0..<3 {
            let y = 340-Double(i*10)
            line(c,hx-140-Double(i*25),y,hx-55,y,ice,3)
        }
    }
    sprite(c,knightFrame,hx,t < 1.82 ? 350-bob : 350,275)
    if t > 1.95 && t < 2.55 {
        spark(c,610,208,35+65*ease((t-1.95)/0.45),gold)
    }
    if t > 1.97 && t < 3.2 { burst(c,625,190,(t-1.97)/0.95,red) }
    if t > 2.0 && t < 2.13 { fill(c,0,0,800,450,rgb(0xffffff,0.55*(1-ease((t-2.0)/0.13)))) }
    if t > 1.85 && t < 2.72 { words("SLASH!",400,75,45,cream) }
    endCard(c,t,human:true)
}
func arrowScene(_ c: CGContext, _ t: Double) {
    if t < 2.02 { sprite(c,robotSprite,610,350,260) }
    else { headlessRobot(c,610,350,260,ease((t-2.02)/0.45)) }
    let archerFrame = t < 1.42 ? bowSprite : bowReleaseSprite
    let recoil = t>1.42 && t<1.65 ? 12*sin(Double.pi*clamp((t-1.42)/0.23)) : 0
    sprite(c,archerFrame,190-recoil,350,275)
    if t>1.42 && t<2.05 {
        let p = ease((t-1.42)/0.6)
        let x = 305+310*p
        line(c,x-70,205,x+14,205,cream,6)
        line(c,x+14,205,x-7,194,gold,5)
        line(c,x+14,205,x-7,216,gold,5)
        for i in 0..<3 { line(c,x-98-Double(i*16),205,x-77-Double(i*16),205,ice,3) }
    }
    if t>2.02 && t<3.2 {
        burst(c,610,170,(t-2.02)/1.05,gold)
        spark(c,610,170,75*ease((t-2.02)/0.25),cream,10)
    }
    if t>2.02 && t<2.15 { fill(c,0,0,800,450,rgb(0xffffff,0.5*(1-ease((t-2.02)/0.13)))) }
    if t>2.05 && t<2.9 { words("HEADSHOT!",400,72,43,cream) }
    endCard(c,t,human:true)
}
func fireScene(_ c: CGContext, _ t: Double) {
    sprite(c,t<0.85 ? robotSprite : robotFireSprite,205,350,268,flipX:t>=0.85)
    let leap = t>1.82 ? 78*sin(Double.pi*clamp((t-1.82)/1.1)) : 0
    let victim = t<1.82 ? knightSprite : knightShockSprite
    sprite(c,victim,615,350-leap,270,opacity:t>2.35 ? 0.7 : 1)
    if t>1.35 && t<2.85 {
        let extend = ease((t-1.35)/0.32)
        let fade = 1-ease((t-2.45)/0.35)
        let endX = 315+295*extend
        c.saveGState(); c.setAlpha(CGFloat(fade))
        flameBand(c,305,endX,270,31,t,rgb(0xb9314d))
        flameBand(c,311,endX-8,270,21,t,rgb(0xff873d))
        flameBand(c,319,endX-18,270,10,t,gold)
        flameBand(c,326,endX-33,270,4,t,cream)
        for i in 0..<9 {
            let x = 350+Double(i)*28*extend
            let y = 246-Double(i%3)*8-sin(t*15+Double(i))*10
            oval(c,x,y,3+Double(i%3),3+Double(i%3),gold)
        }
        c.restoreGState()
        spark(c,605,265,18+34*ease((t-1.82)/0.3),gold,8)
    }
    if t>2.0 && t<3.15 {
        for i in 0..<7 {
            let p = clamp((t-2.0-Double(i)*0.1)/0.9)
            oval(c,580+Double(i*12),230-p*110,10+p*12,9+p*8,rgb(0x596071,0.5*(1-p)))
        }
    }
    if t>1.9 && t<2.8 { words("HOT! HOT!",400,72,43,cream) }
    endCard(c,t,human:false)
}
func electricScene(_ c: CGContext, _ t: Double) {
    let robotFrame = t < 0.9 ? robotSprite : robotAimSprite
    sprite(c,robotFrame,205,350,268,flipX:t >= 0.9)
    let shock = t > 1.55
    let change = ease((t-2.45)/0.2)
    if change < 1 {
        let jitter = shock && t < 2.5 ? Double(Int(t*38)%3-1)*5 : 0
        sprite(c,shock ? knightShockSprite : knightSprite,615+jitter,350,270,
               opacity:1-change)
    }
    if t > 2.55 {
        let p = ease((t-2.55)/0.24)
        let bounce = 24*sin(Double.pi*clamp((t-2.55)/0.75))
        sprite(c,chickenSprite,615,350-bounce,170+40*p,opacity:p)
    }
    if t>1.45 && t<2.55 {
        let p = ease((t-1.45)/0.2)
        let a = 290.0, b = 595.0
        var points: [(Double,Double)] = [(a,255)]
        for i in 1...10 {
            let x = a+(b-a)*Double(i)/10*p
            let offset = i%2 == 0 ? -23.0 : 24.0
            points.append((x,245+offset))
        }
        for i in 1..<points.count {
            line(c,points[i-1].0,points[i-1].1,points[i].0,points[i].1,ice,12)
            line(c,points[i-1].0,points[i-1].1,points[i].0,points[i].1,cream,4)
        }
        spark(c,615,215,30+30*abs(sin(t*12)),ice,12)
    }
    if t > 2.4 && t < 2.68 { fill(c,0,0,800,450,rgb(0xffffff,0.72*(1-ease((t-2.4)/0.28)))) }
    if t > 2.48 && t < 3.28 { burst(c,615,225,(t-2.48)/0.7,ice) }
    if t>2.7 && t<3.25 { words("CỤC TÁC?!",400,84,42,cream) }
    endCard(c,t,human:false)
}
func render(_ c: CGContext, _ frame: Int) {
    let t = Double(frame)/Double(fps)
    stage(c,t)
    switch scene {
    case "sword": swordScene(c,t)
    case "arrow": arrowScene(c,t)
    case "fire": fireScene(c,t)
    default: electricScene(c,t)
    }
}
for frame in 0..<frames {
    autoreleasepool {
        guard let bitmap = NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:w,pixelsHigh:h,
                                            bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,
                                            isPlanar:false,colorSpaceName:.deviceRGB,
                                            bytesPerRow:w*4,bitsPerPixel:32),
              let graphics = NSGraphicsContext(bitmapImageRep:bitmap) else { exit(3) }
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = graphics
        render(graphics.cgContext,frame)
        graphics.flushGraphics(); NSGraphicsContext.restoreGraphicsState()
        guard let jpeg = bitmap.representation(using:.jpeg,properties:[.compressionFactor:0.9]) else { exit(4) }
        FileHandle.standardOutput.write(jpeg)
    }
}
