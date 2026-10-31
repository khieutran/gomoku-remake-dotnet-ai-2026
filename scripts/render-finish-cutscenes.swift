import AppKit
import Foundation

import CoreText

// Four independent character cutscenes. Writes 24 fps MJPEG frames to stdout.
// Encode the stream as VP8/WebM with ffmpeg's image2pipe demuxer (see scripts/render-finish-videos.sh).
// Scenes are composed on an 800x450 stage; `tall` scales that stage into a 600x800 frame for phone
// boards (played with object-fit: cover) and extends the sky and floor around it.

let w = 800, h = 450, fps = 30, frames = 100
let scene = CommandLine.arguments.dropFirst().first ?? ""
let format = CommandLine.arguments.dropFirst(2).first ?? "wide"
guard ["sword", "arrow", "fire", "electric"].contains(scene), ["wide", "tall"].contains(format) else {
    fputs("Usage: render-finish-cutscenes [sword|arrow|fire|electric] [wide|tall]\n", stderr)
    exit(2)
}
let outW = format == "tall" ? 600 : w, outH = format == "tall" ? 800 : h
// The tall cut keeps stage x 40...760 (where the characters act) and the action inside the middle
// 600x600, so a square board can crop the extra sky and floor.
let stageScale = format == "tall" ? 600.0/720.0 : 1.0
let stageLeft = format == "tall" ? -40.0*stageScale : 0.0
let stageTop = format == "tall" ? 255.0 : 0.0

func rgb(_ value: UInt32, _ alpha: Double = 1) -> CGColor {
    CGColor(red: CGFloat((value >> 16) & 255)/255,
            green: CGFloat((value >> 8) & 255)/255,
            blue: CGFloat(value & 255)/255, alpha: CGFloat(alpha))
}
// Colors follow frontend/src/styles/tokens.css so the cutscene sits inside the game window palette.
let ink = rgb(0x493039), cream = rgb(0xffe8d2), cobalt = rgb(0x3379bd)
let ice = rgb(0x8fd8ea), gold = rgb(0xffd36e), red = rgb(0xe45b6a)
let steel = rgb(0x91afc0), bot = rgb(0x62c4bd), sky = rgb(0xdce9ff), wood = rgb(0xf4d8af)
let assetDir = "frontend/public/images/sprites"
// VT323 ships split by script; register both files so Vietnamese letters cascade to the second face.
let pixelFont: NSFont = {
    let dir = "node_modules/@fontsource/vt323/files"
    let faces = ["latin", "vietnamese"].compactMap { subset -> CTFontDescriptor? in
        let url = URL(fileURLWithPath:"\(dir)/vt323-\(subset)-400-normal.woff") as CFURL
        return (CTFontManagerCreateFontDescriptorsFromURL(url) as? [CTFontDescriptor])?.first
    }
    guard let primary = faces.first else { return NSFont.monospacedSystemFont(ofSize:12,weight:.heavy) }
    let cascade = CTFontDescriptorCreateCopyWithAttributes(primary,
        [kCTFontCascadeListAttribute: Array(faces.dropFirst())] as CFDictionary)
    return CTFontCreateWithFontDescriptor(cascade, 12, nil) as NSFont
}()
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
    let font = NSFont(descriptor:pixelFont.fontDescriptor, size:CGFloat(size*1.6)) ?? pixelFont
    let attrs: [NSAttributedString.Key:Any] = [.font:font,.foregroundColor:NSColor(cgColor:shade) ?? .white,
        .strokeColor:NSColor(cgColor:ink) ?? .black, .strokeWidth:-4.5]
    let width = (value as NSString).size(withAttributes:attrs).width
    (value as NSString).draw(at:NSPoint(x:x-width/2,y:Double(h)-y-size*1.6),withAttributes:attrs)
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
// Pixel-art arena in output pixels, snapped to a 4px grid: dusk sky, drifting clouds, two hill layers,
// the knight's castle (left) and the robot's factory (right), and the gomoku board as the floor.
let pixel = 4.0
func block(_ c: CGContext, _ x: Double, _ y: Double, _ width: Double, _ height: Double, _ shade: CGColor) {
    let sx = (x/pixel).rounded(.down)*pixel, sy = (y/pixel).rounded(.down)*pixel
    let ex = ((x+width)/pixel).rounded(.up)*pixel, ey = ((y+height)/pixel).rounded(.up)*pixel
    c.setFillColor(shade); c.fill(CGRect(x:sx,y:Double(outH)-ey,width:ex-sx,height:ey-sy))
}
func ridge(_ c: CGContext, from x0: Double, to x1: Double, base: Double, _ height: (Double) -> Double, _ shade: CGColor) {
    var x = x0
    while x < x1 { block(c,x,base-height(x),pixel,height(x)+pixel,shade); x += pixel }
}
func cloud(_ c: CGContext, _ x: Double, _ y: Double, _ k: Double) {
    let body = rgb(0xf6c9d4), light = rgb(0xffe9e2)
    block(c,x,y+8*k,96*k,16*k,body); block(c,x+16*k,y,44*k,12*k,body); block(c,x+48*k,y-8*k,28*k,12*k,body)
    block(c,x+20*k,y+4*k,36*k,4*k,light); block(c,x+52*k,y-4*k,20*k,4*k,light)
}
func backdrop(_ c: CGContext, _ t: Double) {
    let k = stageScale, width = Double(outW), floorY = stageTop + 350*k
    // Sky: dusk bands from violet to warm peach at the horizon, with a one-row checker dither between bands.
    let bands: [UInt32] = [0x4b3f72, 0x5e4c86, 0x7a5c9a, 0xa270a8, 0xcf86a6, 0xeea2a0, 0xf8c098, 0xffdca0]
    let bandHeight = floorY/Double(bands.count)
    for (i, band) in bands.enumerated() {
        let y = Double(i)*bandHeight
        block(c,0,y,width,bandHeight+pixel,rgb(band))
        if i > 0 {
            var x = Double(i % 2)*pixel
            while x < width { block(c,x,y-pixel,pixel,pixel,rgb(band)); x += pixel*2 }
        }
    }
    // Stars twinkle in the upper sky.
    for i in 0..<18 {
        let x = Double((i*137+41) % Int(width)), y = Double((i*53+17) % Int(max(40, floorY*0.35)))
        if Int(t*3+Double(i)) % 4 != 0 { block(c,x,y,pixel,pixel,rgb(0xfff1d6,0.8)) }
    }
    // Low sun behind the hills.
    let sunX = width*0.5, sunY = floorY-70*k, radius = 64*k
    var row = -radius
    while row < radius {
        let half = (radius*radius-row*row).squareRoot()
        block(c,sunX-half,sunY+row,half*2,pixel,rgb(row < -radius*0.4 ? 0xfff0bf : 0xffd67a))
        row += pixel
    }
    // Clouds drift slowly to the right and wrap.
    for (i, base) in [(40.0, 0.18), (330.0, 0.1), (600.0, 0.26)].enumerated() {
        let x = (base.0*width/800 + t*(6+Double(i)*3)).truncatingRemainder(dividingBy: width+120) - 60
        cloud(c,x,floorY*base.1,k)
    }
    // Far and near hills.
    ridge(c,from:0,to:width,base:floorY,{ x in 70*k+18*k*sin(x/63)+10*k*sin(x/23) },rgb(0x8a6aa6))
    ridge(c,from:0,to:width,base:floorY,{ x in 34*k+12*k*sin(x/41+1.7)+6*k*sin(x/17) },rgb(0x6a5288))
    let silhouette = rgb(0x46365f)
    // Castle on the left, with a waving flag.
    let cx = width*0.06
    block(c,cx,floorY-90*k,96*k,90*k,silhouette)
    for tower in [0.0, 72.0] {
        block(c,cx+tower*k,floorY-130*k,24*k,130*k,silhouette)
        for notch in stride(from: 0.0, to: 24.0, by: 8.0) { block(c,cx+(tower+notch)*k,floorY-138*k,4*k,8*k,silhouette) }
    }
    block(c,cx+40*k,floorY-40*k,16*k,40*k,rgb(0x2f2442))
    block(c,cx+10*k,floorY-160*k,4*k,30*k,silhouette)
    let wave = Int(t*6) % 2 == 0 ? 0.0 : 4.0
    block(c,cx+14*k,floorY-160*k,24*k,8*k,rgb(0xec6983)); block(c,cx+14*k,floorY-152*k+wave*k,16*k,6*k,rgb(0xec6983))
    // Factory on the right: chimney smoke and a blinking antenna light.
    let fx = width*0.78
    block(c,fx,floorY-70*k,120*k,70*k,silhouette)
    block(c,fx+12*k,floorY-118*k,20*k,48*k,silhouette)
    block(c,fx+86*k,floorY-150*k,6*k,80*k,silhouette)
    for window in [0.0, 30.0, 60.0, 90.0] { block(c,fx+(8+window)*k,floorY-50*k,16*k,12*k,rgb(0xffd36e,0.85)) }
    if Int(t*2) % 2 == 0 { block(c,fx+84*k,floorY-158*k,10*k,8*k,rgb(0xff5a5a)) }
    for i in 0..<4 {
        let rise = (t*18 + Double(i)*22).truncatingRemainder(dividingBy: 88)
        block(c,fx+(14+Double(i%2)*8)*k,floorY-(126+rise)*k,(14+rise*0.15)*k,(10+rise*0.1)*k,rgb(0xd9c3d9,0.55*(1-rise/88)))
    }
    // Floor: the gomoku board, with grid lines and a darker front edge.
    block(c,0,floorY,width,Double(outH)-floorY,rgb(0xf0cf9c))
    block(c,0,floorY,width,5*k,rgb(0x70494c))
    var line = floorY+26*k
    while line < Double(outH) { block(c,0,line,width,2*k,rgb(0xc9a072)); line += 34*k }
    var col = 18*k
    while col < width { block(c,col,floorY+5*k,2*k,Double(outH)-floorY,rgb(0xc9a072)); col += 46*k }
    // A pixel X and O resting on the board.
    let xs = width*0.1, xy = floorY+36*k
    for i in 0..<5 {
        let d = Double(i)*4*k
        block(c,xs+d,xy+d,4*k,4*k,rgb(0xa62d55)); block(c,xs+16*k-d,xy+d,4*k,4*k,rgb(0xa62d55))
    }
    let os = width*0.86, oy = floorY+64*k
    block(c,os+4*k,oy,12*k,4*k,rgb(0x1b6268)); block(c,os+4*k,oy+16*k,12*k,4*k,rgb(0x1b6268))
    block(c,os,oy+4*k,4*k,12*k,rgb(0x1b6268)); block(c,os+16*k,oy+4*k,4*k,12*k,rgb(0x1b6268))
}
func stage(_ c: CGContext, _ t: Double) {
    oval(c,205,352,90,10,rgb(0x46365f,0.28)); oval(c,612,352,90,10,rgb(0x46365f,0.28))
}
// The robot's reaching hand in the flipped attack sprites (fingertip at sprite pixel 3,39 of 96x98).
let botHandX = 331.0, botHandY = 190.0, knightChestY = 215.0
func chargeGlow(_ c: CGContext, _ t: Double, _ shade: CGColor) {
    let r = 9+3*sin(t*24)
    oval(c,botHandX,botHandY,r+6,r+6,rgb(0xffffff,0.35)); oval(c,botHandX,botHandY,r,r,shade)
}
// Full-frame flash in stage units, wide enough to cover the square frame's extended sky and floor.
func flash(_ c: CGContext, _ shade: CGColor) { fill(c,-400,-400,1600,1300,shade) }
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
    if t > 2.0 && t < 2.13 { flash(c,rgb(0xffffff,0.55*(1-ease((t-2.0)/0.13)))) }
    if t > 1.85 && t < 2.72 { words("XOẸT!",400,75,45,gold) }
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
    if t>2.02 && t<2.15 { flash(c,rgb(0xffffff,0.5*(1-ease((t-2.02)/0.13)))) }
    if t>2.05 && t<2.9 { words("TRÚNG ĐẦU!",400,72,43,gold) }
}
func fireScene(_ c: CGContext, _ t: Double) {
    sprite(c,t<0.85 ? robotSprite : robotFireSprite,205,350,268,flipX:t>=0.85)
    if t>0.9 && t<2.7 { chargeGlow(c,t,rgb(0xff873d)) }
    let leap = t>1.82 ? 78*sin(Double.pi*clamp((t-1.82)/1.1)) : 0
    let victim = t<1.82 ? knightSprite : knightShockSprite
    sprite(c,victim,615,350-leap,270,opacity:t>2.35 ? 0.7 : 1)
    if t>1.35 && t<2.85 {
        let extend = ease((t-1.35)/0.32)
        let fade = 1-ease((t-2.45)/0.35)
        let endX = botHandX+10+280*extend
        c.saveGState(); c.setAlpha(CGFloat(fade))
        flameBand(c,botHandX-4,endX,botHandY,31,t,rgb(0xb9314d))
        flameBand(c,botHandX,endX-8,botHandY,21,t,rgb(0xff873d))
        flameBand(c,botHandX+4,endX-18,botHandY,10,t,gold)
        flameBand(c,botHandX+8,endX-33,botHandY,4,t,cream)
        for i in 0..<9 {
            let x = botHandX+20+Double(i)*28*extend
            let y = botHandY-22-Double(i%3)*8-sin(t*15+Double(i))*10
            oval(c,x,y,3+Double(i%3),3+Double(i%3),gold)
        }
        c.restoreGState()
        spark(c,605,knightChestY-10,18+34*ease((t-1.82)/0.3),gold,8)
    }
    if t>2.0 && t<3.15 {
        for i in 0..<7 {
            let p = clamp((t-2.0-Double(i)*0.1)/0.9)
            oval(c,580+Double(i*12),190-p*110,10+p*12,9+p*8,rgb(0x596071,0.5*(1-p)))
        }
    }
    if t>1.9 && t<2.8 { words("NÓNG QUÁ!",400,72,43,gold) }
}
func electricScene(_ c: CGContext, _ t: Double) {
    let robotFrame = t < 0.9 ? robotSprite : robotAimSprite
    sprite(c,robotFrame,205,350,268,flipX:t >= 0.9)
    if t>0.95 && t<2.6 { chargeGlow(c,t,ice) }
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
        let a = botHandX, b = 595.0
        var points: [(Double,Double)] = [(a,botHandY)]
        for i in 1...10 {
            let x = a+(b-a)*Double(i)/10*p
            let offset = i%2 == 0 ? -23.0 : 24.0
            let along = (x-a)/(b-a)
            points.append((x,botHandY+(knightChestY-botHandY)*along+(i == 10 ? 0 : offset*0.8)))
        }
        for i in 1..<points.count {
            line(c,points[i-1].0,points[i-1].1,points[i].0,points[i].1,ice,12)
            line(c,points[i-1].0,points[i-1].1,points[i].0,points[i].1,cream,4)
        }
        spark(c,615,215,30+30*abs(sin(t*12)),ice,12)
    }
    if t > 2.4 && t < 2.68 { flash(c,rgb(0xffffff,0.72*(1-ease((t-2.4)/0.28)))) }
    if t > 2.48 && t < 3.28 { burst(c,615,225,(t-2.48)/0.7,ice) }
    if t>2.7 && t<3.3 { words("CỤC TÁC?!",400,84,42,gold) }
}
func render(_ c: CGContext, _ frame: Int) {
    let t = Double(frame)/Double(fps)
    backdrop(c,t)
    c.translateBy(x:stageLeft,y:Double(outH)-stageTop-Double(h)*stageScale)
    c.scaleBy(x:stageScale,y:stageScale)
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
        guard let bitmap = NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:outW,pixelsHigh:outH,
                                            bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,
                                            isPlanar:false,colorSpaceName:.deviceRGB,
                                            bytesPerRow:outW*4,bitsPerPixel:32),
              let graphics = NSGraphicsContext(bitmapImageRep:bitmap) else { exit(3) }
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = graphics
        render(graphics.cgContext,frame)
        graphics.flushGraphics(); NSGraphicsContext.restoreGraphicsState()
        guard let jpeg = bitmap.representation(using:.jpeg,properties:[.compressionFactor:0.9]) else { exit(4) }
        FileHandle.standardOutput.write(jpeg)
    }
}
