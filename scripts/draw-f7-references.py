"""Generate the original SVG datum schematics used in the two F7 modelling guides."""
from pathlib import Path
from html import escape
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1] / 'docs/models/references'


def sheet(unit, front_overhang, rear_overhang):
    a = unit == 'f7a'
    length = 9.144 + front_overhang + rear_overhang
    rear = -(4.572 + rear_overhang)
    front = 4.572 + front_overhang
    x = lambda station: 600 + station * 50
    y = lambda height: 430 - height * 50
    svg = ['''<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="1280" viewBox="0 0 1200 1280" role="img" aria-labelledby="title desc">
<title id="title">EMD UNIT modelling dimensions</title>
<desc id="desc">Original schematic side and end views and enlarged Blomberg B side view. Principal dimensions follow EMD's 1948 specifications. Bogie outline length and visible height are drawing estimates. Shapes are schematic.</desc>
<defs>
<marker id="arrow" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M0 0L10 5L0 10Z" fill="#244354"/></marker>
<style>
text{font-family:Arial,sans-serif;font-size:16px;fill:#203e50}
.title{font-size:30px;font-weight:bold}.section{font-size:21px;font-weight:bold}.small{font-size:14px}.bold{font-weight:bold}
.body{fill:#e2eaf0;stroke:#456578;stroke-width:1.8;stroke-linejoin:round}
.detail{fill:none;stroke:#78909f;stroke-width:1.4}.window{fill:#9bbbc9;stroke:#456578;stroke-width:1.3}
.rail{stroke:#244354;stroke-width:2}.guide{stroke:#4698a2;stroke-width:1;stroke-dasharray:6 5;fill:none}
.ext{stroke:#9fb4bd;stroke-width:1;fill:none}.dim{stroke:#244354;stroke-width:1.2;fill:none;marker-start:url(#arrow);marker-end:url(#arrow)}
.estimate{stroke:#b9791f;stroke-width:1.7;stroke-dasharray:7 4;fill:none}.amber{fill:#956017}
</style></defs><rect width="1200" height="1280" fill="#f8fbfd"/>'''.replace('UNIT', unit.upper())]
    def text(px, py, value, cls='', anchor='start'):
        svg.append(f'<text x="{px:.3f}" y="{py:.3f}" class="{cls}" text-anchor="{anchor}">{escape(value)}</text>')
    def line(x1,y1,x2,y2,cls='detail'):
        svg.append(f'<path d="M{x1:.3f} {y1:.3f}L{x2:.3f} {y2:.3f}" class="{cls}"/>')
    def hdim(x1,x2,py,label):
        line(x1,py,x2,py,'dim')
        width = len(label)*8.5+18
        svg.append(f'<rect x="{(x1+x2-width)/2:.3f}" y="{py-14:.3f}" width="{width:.3f}" height="25" fill="#f8fbfd"/>')
        text((x1+x2)/2,py+5,label,'bold','middle')
    def circle(cx,cy,r,cls='body'):
        svg.append(f'<circle cx="{cx:.3f}" cy="{cy:.3f}" r="{r:.3f}" class="{cls}"/>')
    def wheel(cx,cy,r):
        circle(cx,cy,r)
        circle(cx,cy,r*.25,'detail')
        line(cx-r*.4,cy,cx+r*.4,cy)
        line(cx,cy-r*.4,cx,cy+r*.4)
    text(40,48,f'EMD {unit.upper()} · Dimensions for modelling','title')
    text(40,78,'Metres · 1:1 · EMD 1948 specification · '+('cab locomotive' if a else 'separate powered booster'))
    text(40,104,'Read dimensions, not contours. Bogie outline estimates are amber; complete mechanical truck height/width are unverified.','small')
    text(40,139,'SIDE VIEW · +green Y toward the designated front →','section')
    for c in (rear,front): line(x(c),154,x(c),585,'ext')
    hdim(x(rear),x(front),172,f'{length:.4f} · coupler pulling faces')
    # The shell is only an illustration inside the dimensioned envelope.
    left,right=x(rear+.4),x(front-.4)
    if a:
        svg.append(f'<path d="M{left} {y(.95)}V{y(3.7)}Q{left} {y(4.2799)} {left+25} {y(4.2799)}H{right-110}Q{right-68} {y(4.2799)} {right-48} {y(3.55)}L{right-15} {y(3.2)}Q{right+5} {y(2.4)} {right} {y(1.1)}Z" class="body"/>')
        svg.append(f'<path d="M{right-97} {y(3.9)}L{right-62} {y(3.8)}L{right-48} {y(3.25)}H{right-88}Z" class="window"/>')
        line(right-115,y(3.95),right-115,y(1.05))
    else:
        svg.append(f'<path d="M{left} {y(.95)}V{y(3.85)}Q{left} {y(4.2799)} {left+25} {y(4.2799)}H{right-25}Q{right} {y(4.2799)} {right} {y(3.85)}V{y(.95)}Z" class="body"/>')
    for p in (-2.7,0,2.7): circle(x(p),y(2.65),9,'window')
    for p in (-.8,.15,1.1,2.05):
        svg.append(f'<path d="M{x(p)-15} {y(4.2799)}Q{x(p)} {y(4.75)} {x(p)+15} {y(4.2799)}Z" class="body"/>')
    line(left,y(3.55),right-125 if a else right,y(3.55))
    line(left,y(3.1),right-125 if a else right,y(3.1))
    svg.append(f'<rect x="{x(-1.8)}" y="{y(.85)}" width="180" height="20" rx="6" class="body"/>')
    for pivot in (-4.572,4.572):
        for station in (pivot-1.3716,pivot+1.3716): wheel(x(station),y(.508),25.4)
        svg.append(f'<rect x="{x(pivot-2.25)}" y="{y(1.2)}" width="225" height="43" rx="13" class="detail"/>')
        line(x(pivot),y(4.8),x(pivot),512,'guide')
        circle(x(pivot),430,3,'window')
    line(x(rear),y(.88),left,y(.88),'rail')
    line(right,y(.88),x(front),y(.88),'rail')
    line(140,430,1060,430,'rail')
    line(600,205,600,453,'guide')
    text(600,455,'Origin: bogie midpoint at railhead','small','middle')
    hdim(x(-4.572),x(4.572),484,'9.1440 · bogie centres')
    hdim(x(-5.9436),x(-3.2004),523,'2.7432 · axles')
    for station in (-5.9436,-3.2004): line(x(station),430,x(station),535,'ext')
    for pivot in (-4.572,4.572): line(x(pivot),512,x(pivot),583,'ext')
    hdim(x(rear),x(-4.572),570,f'{rear_overhang:.4f}')
    hdim(x(4.572),x(front),570,f'{front_overhang:.4f}')
    text(600,571,'Pivot-to-coupling-face overhangs','small','middle')
    text(40,611,('F7A: rear Y = −7.6200; front Y = +7.8232. Coupler envelope centre = +0.1016, not zero.' if a else 'F7B: rear Y = −7.6200; front Y = +7.6200. Both coupler overhangs are 3.0480.'),'bold')
    text(40,655,'END DATUMS · schematic','section')
    text(570,655,'BLOMBERG B · ENLARGED SIDE VIEW','section')
    # End view: 60 px/m, rail at 945, centre x=265.
    ez=lambda height:945-height*60
    w=2.9972*60/2
    svg.append(f'<path d="M{265-w} {ez(1.2)}V{ez(3.2)}Q{265-w} {ez(4.2799)} 265 {ez(4.2799)}Q{265+w} {ez(4.2799)} {265+w} {ez(3.2)}V{ez(1.2)}Z" class="body"/>')
    if a:
        for dx in (-47,6):
            svg.append(f'<rect x="{265+dx}" y="{ez(3.65)}" width="41" height="30" rx="4" class="window"/>')
        circle(265,ez(2.8),12,'window')
    else:
        svg.append(f'<rect x="244" y="{ez(3.25)}" width="42" height="127" class="detail"/>')
    line(140,945,410,945,'rail')
    line(265,668,265,960,'guide')
    for side in (-1,1):
        line(265+side*3.2512*30,ez(.95),265+side*3.2512*30,ez(4.572),'guide')
        line(265+side*1.435*30,945,265+side*1.435*30,970,'ext')
    line(110,ez(4.572),110,945,'dim')
    svg.append(f'<text transform="translate(91 {(ez(4.572)+945)/2}) rotate(-90)" text-anchor="middle" class="bold">4.5720 · overall envelope</text>')
    line(110,ez(4.572),375,ez(4.572),'guide')
    line(155,ez(4.2799),425,ez(4.2799),'guide')
    text(435,ez(4.2799)+4,'4.2799','small')
    text(435,ez(4.2799)+23,'carlines','small')
    hdim(265-1.435*30,265+1.435*30,973,'1.4350 gauge')
    for sign in (-1,1): line(265+sign*w,ez(3),265+sign*w,1010,'ext')
    hdim(265-w,265+w,1010,'2.9972 body posts')
    hdim(265-3.2512*30,265+3.2512*30,1054,'3.2512 handholds')
    # Bogie side: 110 px/m, pivot x=850, rail y=945.
    bx=lambda station:850+station*110
    bz=lambda height:945-height*110
    for station in (-1.3716,1.3716): wheel(bx(station),bz(.508),55.88)
    svg.append(f'<path d="M{bx(-2.25)} {bz(.23)}L{bx(-2.05)} {bz(.96)}Q{bx(-1.5)} {bz(1.2)} {bx(-.9)} {bz(.97)}H{bx(.9)}Q{bx(1.5)} {bz(1.2)} {bx(2.05)} {bz(.96)}L{bx(2.25)} {bz(.23)}" class="detail"/>')
    svg.append(f'<rect x="{bx(-2.25)}" y="{bz(1.2)}" width="495" height="132" class="estimate"/>')
    line(575,945,1125,945,'rail')
    line(850,785,850,971,'guide')
    for station in (-1.3716,1.3716): line(bx(station),bz(.508),bx(station),1010,'ext')
    hdim(bx(-1.3716),bx(1.3716),1009,'2.7432 · factory wheelbase')
    hdim(bx(-2.25),bx(2.25),1054,'≈4.5 · visible outline estimate')
    line(1135,bz(1.2),1135,945,'dim')
    text(850,760,'≈1.2 above rail · visible outline estimate','amber','middle')
    text(850,786,'Wheel tread Ø1.0160; axle Z = 0.5080','bold','middle')
    text(850,969,'Railhead Z = 0; component pivot at the centreline','small','middle')
    text(40,1110,'FACTORY DATUMS AND WORKING ESTIMATES','section')
    text(40,1141,'Main dimensions: EMD specification '+('8013' if a else '8014')+', August 3, 1948. Full references and coordinates are in the adjacent guide.')
    text(40,1168,'Truck outline ≈4.5 m × 1.2 m is estimated from the EMD B-unit elevation, calibrated to the 2.7432 m wheelbase.','small')
    text(40,1194,'The 1.2 m value is the visible top above rail, not full mechanical height including hidden bolster and flanges.','small')
    text(40,1220,'Overall truck width and prototype coupler height need separate verification. Gauge is not truck width.','small')
    text(40,1246,'SketchUp: red X across, green +Y forward, blue +Z up. Separate body, bogies and wheelsets; mark livery faces with paint.','small')
    svg.append('</svg>')
    path=ROOT/unit/f'{unit}-modelling-measures.svg'
    # Inline the simple class rules too, so PDF/SVG importers without CSS support
    # still preserve drawing colours, unfilled guides and readable type sizes.
    markup = '\n'.join(svg)
    rules = {selector.strip(): dict(item.split(':',1) for item in declarations.split(';') if ':' in item)
             for selector,declarations in re.findall(r'([^{}]+)\{([^{}]+)\}', markup.split('<style>')[1].split('</style>')[0])}
    ET.register_namespace('', 'http://www.w3.org/2000/svg')
    document=ET.fromstring(markup)
    for element in document.iter():
        if element.tag.endswith('}text'):
            for key,value in rules.get('text',{}).items(): element.set(key,value)
        for cls in element.get('class','').split():
            for key,value in rules.get('.'+cls,{}).items(): element.set(key,value)
    ET.indent(document,space='  ')
    path.write_text(ET.tostring(document,encoding='unicode')+'\n',encoding='utf-8')
    print(path)


if __name__ == '__main__':
    sheet('f7a',3.2512,3.0480)
    sheet('f7b',3.0480,3.0480)
