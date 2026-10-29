import AppKit
import Foundation

// Writes an MJPEG frame sequence to stdout. Pipe it into the bundled VP8 encoder.
// Example: swiftc scripts/render-finish-videos.swift -o /tmp/gomoku-finish-render
//          /tmp/gomoku-finish-render sword | ffmpeg -f image2pipe -framerate 24 -vcodec mjpeg -i - -c:v libvpx sword.webm

private let width = 800
private let height = 450
private let frameRate = 24
private let frameCount = 96
private let boardX = 70.0
private let boardY = 62.0
private let boardW = 660.0
private let boardH = 330.0
private let cell = 33.0

private func color(_ value: UInt32, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(red: CGFloat((value >> 16) & 255) / 255,
            green: CGFloat((value >> 8) & 255) / 255,
            blue: CGFloat(value & 255) / 255, alpha: alpha)
}

private enum Palette {
    static let ink = color(0x493039)
    static let line = color(0x70494c)
    static let paper = color(0xffead4)
    static let wood = color(0xf4d8af)
    static let grid = color(0xb88c72)
    static let blue = color(0xdce8fb)
    static let teal = color(0x61c2ba)
    static let yellow = color(0xffd36e)
    static let x = color(0xa62d55)
    static let o = color(0x1b6268)
}

private func clamp(_ value: Double, _ lo: Double = 0, _ hi: Double = 1) -> Double {
    min(hi, max(lo, value))
}

private func ease(_ value: Double) -> Double {
    let x = clamp(value)
    return x * x * (3 - 2 * x)
}

private func rect(_ ctx: CGContext, _ x: Double, _ y: Double, _ w: Double, _ h: Double,
                  _ fill: CGColor, stroke: CGColor? = nil, lineWidth: Double = 1) {
    let r = CGRect(x: x, y: Double(height) - y - h, width: w, height: h)
    ctx.setFillColor(fill)
    ctx.fill(r)
    if let stroke {
        ctx.setStrokeColor(stroke)
        ctx.setLineWidth(lineWidth)
        ctx.stroke(r)
    }
}

private func circle(_ ctx: CGContext, _ x: Double, _ y: Double, _ radius: Double,
                    _ fill: CGColor, stroke: CGColor? = nil, lineWidth: Double = 1) {
    let r = CGRect(x: x-radius, y: Double(height)-y-radius, width:radius*2, height:radius*2)
    ctx.setFillColor(fill)
    ctx.fillEllipse(in:r)
    if let stroke {
        ctx.setStrokeColor(stroke)
        ctx.setLineWidth(lineWidth)
        ctx.strokeEllipse(in:r)
    }
}

private func line(_ ctx: CGContext, _ x1: Double, _ y1: Double,
                  _ x2: Double, _ y2: Double, _ shade: CGColor, _ weight: Double = 2) {
    ctx.setStrokeColor(shade)
    ctx.setLineWidth(weight)
    ctx.setLineCap(.square)
    ctx.move(to:CGPoint(x:x1,y:Double(height)-y1))
    ctx.addLine(to:CGPoint(x:x2,y:Double(height)-y2))
    ctx.strokePath()
}

private func text(_ value: String, _ x: Double, _ y: Double, _ size: Double,
                  _ shade: CGColor, bold: Bool = true) {
    let font = NSFont.monospacedSystemFont(ofSize:CGFloat(size), weight:bold ? .heavy : .medium)
    let attributes: [NSAttributedString.Key: Any] = [
        .font: font,
        .foregroundColor: NSColor(cgColor:shade) ?? .black,
    ]
    (value as NSString).draw(at:NSPoint(x:x,y:Double(height)-y-size*1.16),withAttributes:attributes)
}

private func centeredText(_ value: String, _ centerX: Double, _ y: Double, _ size: Double,
                          _ shade: CGColor) {
    let font = NSFont.monospacedSystemFont(ofSize:CGFloat(size),weight:.heavy)
    let w = (value as NSString).size(withAttributes:[.font:font]).width
    text(value,centerX-Double(w)/2,y,size,shade)
}

private func stone(_ ctx: CGContext, _ symbol: String, _ x: Double, _ y: Double,
                   _ scale: Double = 1, _ opacity: Double = 1) {
    ctx.saveGState()
    ctx.setAlpha(CGFloat(opacity))
    let shade = symbol == "X" ? Palette.x : Palette.o
    if symbol == "X" {
        let r = 9.5 * scale
        line(ctx,x-r,y-r,x+r,y+r,shade,5*scale)
        line(ctx,x+r,y-r,x-r,y+r,shade,5*scale)
    } else {
        circle(ctx,x,y,10*scale,color(0xf4d8af,0),stroke:shade,lineWidth:5*scale)
    }
    ctx.restoreGState()
}

private let otherStones: [(Int,Int)] = [(3,4),(2,9),(3,13),(5,5),(6,8),(6,11),(7,15),(2,14)]

private func board(_ ctx: CGContext, winner: String, now: Double,
                   kind: String, split: Double = 0, shrink: Double = 0) {
    ctx.saveGState()
    if shrink > 0 {
        let size = 1 - 0.93 * shrink
        let toX = 694.0, toY = 359.0
        ctx.translateBy(x:CGFloat((1-size)*toX),y:CGFloat((1-size)*(Double(height)-toY)))
        ctx.scaleBy(x:CGFloat(size),y:CGFloat(size))
        ctx.setAlpha(CGFloat(1 - 0.85*shrink))
    }
    let halves: [(Int,Int,Double)] = split > 0 ? [(0,5,-split),(5,10,split)] : [(0,10,0)]
    for (first,last,offset) in halves {
        let y = boardY + Double(first)*cell + offset
        let h = Double(last-first)*cell
        rect(ctx,boardX+5, y+7, boardW,h,color(0x493039,0.4))
        rect(ctx,boardX,y,boardW,h,Palette.wood,stroke:Palette.line,lineWidth:4)
        for col in 1..<20 { line(ctx,boardX+Double(col)*cell,y,boardX+Double(col)*cell,y+h,Palette.grid,1.2) }
        for row in (first+1)..<last {
            let gy = boardY+Double(row)*cell+offset
            line(ctx,boardX,gy,boardX+boardW,gy,Palette.grid,1.2)
        }
        for row in first..<last {
            for col in 0..<20 {
                let isWinner = row == 4 && (7...11).contains(col)
                let isOther = otherStones.contains { $0.0 == row && $0.1 == col }
                if !isWinner && !isOther { continue }
                let sx = boardX+(Double(col)+0.5)*cell
                let sy = boardY+(Double(row)+0.5)*cell+offset
                if isWinner {
                    let glow = ease((now-0.25-Double(col-7)*0.10)/0.18)
                    if glow > 0 {
                        rect(ctx,boardX+Double(col)*cell+2,
                             boardY+Double(row)*cell+offset+2,
                             cell-4,cell-4,color(0xffe779,CGFloat(glow)),
                             stroke:color(0xb34b51,CGFloat(glow)),lineWidth:2)
                    }
                    stone(ctx,winner,sx,sy)
                } else {
                    let other = winner == "X" ? "O" : "X"
                    if kind == "sword" && now > 1.9 {
                        let fall = ease((now-1.9)/0.7)
                        let dx = Double((col*13+row*7)%3-1)*18*fall
                        stone(ctx,other,sx+dx,sy+fall*46,1-0.5*fall,1-fall)
                    } else if kind == "arrows" && now > 1.75 {
                        let boom = ease((now-1.75)/0.7)
                        stone(ctx,other,sx,sy,1+0.6*boom,1-boom)
                    } else if kind == "hammer" && now > 1.8 {
                        let squish = ease((now-1.8)/0.35)
                        ctx.saveGState()
                        ctx.translateBy(x:0,y:CGFloat((Double(height)-sy)*squish*0.85))
                        ctx.scaleBy(x:1,y:CGFloat(1-0.82*squish))
                        stone(ctx,other,sx,sy,1,1-0.45*squish)
                        ctx.restoreGState()
                    } else {
                        stone(ctx,other,sx,sy)
                    }
                }
            }
        }
    }
    ctx.restoreGState()
}

private func backdrop(_ ctx: CGContext, kind: String, winner: String) {
    rect(ctx,0,0,Double(width),Double(height),Palette.blue)
    for x in stride(from:0,through:width,by:30) { line(ctx,Double(x),0,Double(x),Double(height),color(0xffffff,0.8),2) }
    for y in stride(from:0,through:height,by:30) { line(ctx,0,Double(y),Double(width),Double(y),color(0xffffff,0.8),2) }
    rect(ctx,70,18,660,35,Palette.teal,stroke:Palette.line,lineWidth:3)
    text(winner == "X" ? "HUMAN  X   VS   O  AI" : "HUMAN  X   VS   O  AI",89,24,19,Palette.ink)
    text(kind.uppercased(),570,27,14,Palette.ink)
}

private func result(_ ctx: CGContext, winner: String, now: Double) {
    let appear = ease((now-2.95)/0.38)
    if appear <= 0 { return }
    ctx.saveGState()
    ctx.setAlpha(CGFloat(appear))
    let y = 318.0 + (1-appear)*28
    rect(ctx,255,y+6,290,74,Palette.ink)
    rect(ctx,250,y,290,74,Palette.yellow,stroke:Palette.line,lineWidth:4)
    centeredText(winner == "X" ? "BẠN THẮNG!" : "BOT THẮNG!",395,y+17,27,Palette.ink)
    centeredText(winner == "X" ? "X  X  X  X  X" : "O  O  O  O  O",395,y+49,14,Palette.ink)
    ctx.restoreGState()
}

private func sword(_ ctx: CGContext, _ now: Double) {
    let sweep = ease((now-1.35)/0.52)
    if now < 1.15 || now > 2.55 { return }
    let x = 495 - 255*sweep
    let y = 218 - 14*sin(sweep*Double.pi)
    line(ctx,495,210,x,y,Palette.yellow,9)
    line(ctx,495,210,x,y,color(0xffffff),3)
    ctx.saveGState()
    ctx.translateBy(x:CGFloat(x),y:CGFloat(Double(height)-y))
    ctx.rotate(by:CGFloat(-0.18 + sweep*0.32))
    ctx.setFillColor(color(0xc9e2e8)); ctx.fill(CGRect(x:0,y:-8,width:96,height:16))
    ctx.setStrokeColor(Palette.line); ctx.setLineWidth(3); ctx.stroke(CGRect(x:0,y:-8,width:96,height:16))
    ctx.setFillColor(Palette.x); ctx.fill(CGRect(x:-10,y:-16,width:14,height:32))
    ctx.restoreGState()
    if now > 1.85 && now < 2.6 { centeredText("XOẸT!",390,118,48,Palette.x) }
}

private func chip(_ ctx: CGContext, _ now: Double) {
    let appear = ease((now-1.0)/0.2)
    if appear == 0 { return }
    let glitch = now > 1.6 && now < 2.2 ? Double((Int(now*28)%3)-1)*7 : 0
    let x = 351+glitch, y = 173.0
    rect(ctx,x-10,y+10,108,100,color(0x493039,0.5))
    rect(ctx,x,y,108,100,now > 2.15 ? color(0x6f787a) : Palette.teal,stroke:color(0x23545b),lineWidth:7)
    for i in 0..<6 {
        let p = Double(i)*15
        rect(ctx,x-15,y+8+p,14,5,Palette.ink)
        rect(ctx,x+109,y+8+p,14,5,Palette.ink)
    }
    centeredText(now > 2.2 ? "X_X" : "AI",x+54,y+25,35,Palette.ink)
    if now > 1.65 && now < 2.65 {
        rect(ctx,194,105,412,51,color(0x33242e,0.95),stroke:color(0xf09499),lineWidth:4)
        centeredText("ERROR: PLAYER TOO SMART",400,114,22,color(0xffe6db))
    }
    if now > 2.2 {
        let smoke = ease((now-2.2)/0.75)
        for i in 0..<4 {
            let x = 391.0 + Double(i*16-24)
            let y = 162.0 - 80*smoke - Double(i%2)*20
            circle(ctx,x,y,12+smoke*17,color(0x8b8590,CGFloat((1-smoke)*0.65)))
        }
    }
}

private func arrows(_ ctx: CGContext, _ now: Double) {
    if now < 1.3 || now > 2.7 { return }
    for i in 0..<5 {
        let travel = ease((now-1.3-Double(i)*0.09)/0.42)
        if travel == 0 || travel == 1 { continue }
        let x = 320+Double(i)*33+travel*215
        let y = 209+Double(i%3-1)*14
        line(ctx,x-68,y,x,y,Palette.x,6)
        line(ctx,x-16,y-11,x,y,Palette.x,6)
        line(ctx,x-16,y+11,x,y,Palette.x,6)
    }
    if now > 1.75 {
        let burst = 1-ease((now-1.75)/0.7)
        for i in 0..<24 {
            let theta = Double(i)*Double.pi/12
            let radius = (1-burst)*100
            let x = 550+cos(theta)*radius
            let y = 208+sin(theta)*radius
            circle(ctx,x,y,3+burst*4,color(i%2 == 0 ? 0xffd36e : 0xa62d55,CGFloat(burst)))
        }
    }
}

private func chicken(_ ctx: CGContext, _ now: Double) {
    if now < 1.0 || now > 2.85 { return }
    let fall = ease((now-1.9)/0.6)
    let x = 535+fall*70, y = 210+fall*65
    ctx.saveGState(); ctx.setAlpha(CGFloat(1-fall))
    rect(ctx,x-30,y-25,58,54,color(0xfff0d6),stroke:Palette.line,lineWidth:5)
    rect(ctx,x-19,y-39,18,15,color(0xd84b5d),stroke:Palette.line,lineWidth:3)
    rect(ctx,x+16,y-8,24,14,Palette.yellow,stroke:Palette.line,lineWidth:3)
    circle(ctx,x+12,y-11,3,Palette.ink)
    ctx.restoreGState()
    let cut = ease((now-1.55)/0.36)
    if cut > 0 && cut < 1 || now > 1.9 && now < 2.2 {
        let sx = -160+900*cut
        line(ctx,sx,224,sx+260,203,color(0xffffff),12)
        line(ctx,sx,229,sx+260,208,Palette.x,4)
    }
    if now > 1.85 && now < 2.7 { centeredText("CỤC TÁC!",400,114,43,Palette.x) }
    if now > 1.9 {
        for i in 0..<7 {
            let phase = ease((now-1.9)/0.7)
            let angle = Double(i)*Double.pi*2/7
            circle(ctx,540+cos(angle)*80*phase,210+sin(angle)*80*phase,4,color(0xffffff,CGFloat(1-phase)))
        }
    }
}

private func hammer(_ ctx: CGContext, _ now: Double) {
    if now < 1.2 || now > 2.75 { return }
    let drop = ease((now-1.25)/0.5)
    let y = -150+drop*310
    rect(ctx,364,y,38,125,color(0xa66e4c),stroke:Palette.line,lineWidth:5)
    rect(ctx,300,y+95,165,72,color(0x9aa4ae),stroke:Palette.line,lineWidth:6)
    rect(ctx,311,y+102,142,14,color(0xd1dce3))
    if now > 1.75 {
        let dust = 1-ease((now-1.75)/0.65)
        for i in 0..<10 {
            let a = Double(i)*Double.pi/5
            circle(ctx,380+cos(a)*(1-dust)*130,277+sin(a)*(1-dust)*35,
                   8+8*dust,color(0xb49779,CGFloat(dust*0.8)))
        }
        if now < 2.45 { centeredText("RẦM!",400,113,52,Palette.x) }
    }
}

private func deletePlayer(_ ctx: CGContext, _ now: Double) {
    if now < 0.95 { return }
    let selection = ease((now-0.95)/0.65)
    if now < 2.7 {
        rect(ctx,180,126,420*selection,210*selection,color(0xffd36e,0.20),
             stroke:color(0xa62d55,0.9),lineWidth:3)
    }
    let cursorX = 185+420*selection
    let cursorY = 129+210*selection
    if now < 2.85 {
        line(ctx,cursorX,cursorY,cursorX,cursorY+37,color(0xffffff),10)
        line(ctx,cursorX,cursorY,cursorX+22,cursorY+25,color(0xffffff),10)
        line(ctx,cursorX,cursorY,cursorX,cursorY+37,Palette.ink,3)
    }
    if now > 1.6 && now < 2.15 {
        rect(ctx,532,275,130,52,color(0xfff9e8),stroke:Palette.line,lineWidth:4)
        rect(ctx,540,282,114,38,Palette.x)
        text("Delete",550,290,18,color(0xffffff))
    }
    if now > 2.15 && now < 2.85 {
        rect(ctx,250,128,300,120,color(0xfff4df),stroke:Palette.line,lineWidth:5)
        centeredText("DELETE PLAYER?",400,145,20,Palette.ink)
        centeredText("Are you sure?",400,176,16,Palette.ink)
        rect(ctx,357,204,85,30,Palette.yellow,stroke:Palette.line,lineWidth:3)
        centeredText("YES",400,209,16,Palette.ink)
    }
    if now > 2.65 {
        rect(ctx,682,337,55,62,color(0x79a9b5),stroke:Palette.line,lineWidth:4)
        rect(ctx,676,330,66,10,color(0xaac9d0),stroke:Palette.line,lineWidth:3)
        line(ctx,699,348,699,385,Palette.line,3)
        line(ctx,718,348,718,385,Palette.line,3)
    }
}

private func render(_ ctx: CGContext, kind: String, frame: Int) {
    let now = Double(frame)/Double(frameRate)
    let winner = ["sword","shutdown","arrows"].contains(kind) ? "X" : "O"
    backdrop(ctx,kind:kind,winner:winner)
    let split = kind == "chicken" ? 75*ease((now-1.9)/0.6) : 0
    let shrink = kind == "delete" ? ease((now-2.75)/0.55) : 0
    board(ctx,winner:winner,now:now,kind:kind,split:split,shrink:shrink)
    switch kind {
    case "sword": sword(ctx,now)
    case "shutdown": chip(ctx,now)
    case "arrows": arrows(ctx,now)
    case "chicken": chicken(ctx,now)
    case "hammer": hammer(ctx,now)
    case "delete": deletePlayer(ctx,now)
    default: break
    }
    result(ctx,winner:winner,now:now)
}

let valid = Set(["sword","shutdown","arrows","chicken","hammer","delete"])
guard CommandLine.arguments.count == 2, valid.contains(CommandLine.arguments[1]) else {
    fputs("Usage: render-finish-videos [sword|shutdown|arrows|chicken|hammer|delete]\n",stderr)
    exit(2)
}
let kind = CommandLine.arguments[1]
for frame in 0..<frameCount {
    autoreleasepool {
        guard let bitmap = NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:width,pixelsHigh:height,
            bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,
            colorSpaceName:.deviceRGB,bytesPerRow:width*4,bitsPerPixel:32),
            let graphics = NSGraphicsContext(bitmapImageRep:bitmap) else { exit(3) }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = graphics
        render(graphics.cgContext,kind:kind,frame:frame)
        graphics.flushGraphics()
        NSGraphicsContext.restoreGraphicsState()
        guard let jpeg = bitmap.representation(using:.jpeg,
            properties:[.compressionFactor:0.88]) else { exit(4) }
        FileHandle.standardOutput.write(jpeg)
    }
}
