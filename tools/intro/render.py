import math, numpy as np
from PIL import Image,ImageDraw,ImageFont,ImageFilter
from pathlib import Path
import os, argparse
P=Path(__file__).resolve().parent; W,H=960,540; FPS=20; S=2; WW,HH=W*S,H*S
F=Path(os.environ.get('OPSIS_FONT_DIR','/usr/share/fonts/truetype/lato'))
fonts={k:ImageFont.truetype(str(F/f),s*S) for k,f,s in [('title','Lato-Heavy.ttf',62),('sub','Lato-Regular.ttf',25),('label','Lato-Semibold.ttf',15),('gs','Lato-Bold.ttf',17),('tag','Lato-Medium.ttf',24)]}
orb=Image.open(P/'assets/spectrum-reference.png').convert('RGBA').crop((248,19,702,473)); y,x=np.mgrid[:454,:454]; orb.putalpha(Image.fromarray(np.uint8(255*np.clip((226.5-np.hypot(x-226.5,y-226.5))/2.5,0,1))))
# Preserve the original sphere geometry and luminance; shift saturated colors to blue.
import cv2
arr=np.array(orb); hsv=cv2.cvtColor(arr[:,:,:3],cv2.COLOR_RGB2HSV).astype(np.float32)
hue=hsv[:,:,0]; sat=hsv[:,:,1]; hsv[:,:,0]=np.clip(106+(hue-110)*.12,97,116)
hsv[:,:,1]=np.minimum(255,sat*.95)
arr[:,:,:3]=cv2.cvtColor(hsv.astype(np.uint8),cv2.COLOR_HSV2RGB); orb=Image.fromarray(arr,'RGBA')
blue=Image.open(P/'assets/opsis-official.png').convert('RGBA');
gs=Image.open(P/'assets/gs-app-labs.png').convert('RGBA').crop((17,13,150,147))
gsm=Image.new('L',gs.size);ImageDraw.Draw(gsm).rounded_rectangle((0,0,132,133),radius=29,fill=255);gs.putalpha(gsm)
Y,X=np.mgrid[:H,:W]; bg=np.zeros((H,W,3),float);bg[:]=[5,8,19]
for cx,cy,sx,sy,c in [(480,245,340,220,[15,12,43]),(110,470,220,150,[10,8,38]),(900,90,220,145,[2,20,36])]:bg+=np.exp(-(((X-cx)/sx)**2+((Y-cy)/sy)**2))[...,None]*c
base=Image.fromarray(np.uint8(np.clip(bg,0,255))).convert('RGBA').resize((WW,HH),Image.Resampling.BICUBIC);rng=np.random.default_rng(42); particles=rng.random((58,5))
apps=[('Κόμβος','hub',(116,207,255)),('Χρονολόγιο','time',(111,184,255)),('Ελληνική\nΓραμματεία','scroll',(221,193,137)),('Παγκόσμια\nΛογοτεχνία','book',(203,165,251)),('GearWin','gear',(138,211,235)),('NewsCrawler','news',(119,226,210)),('ENFIA Pro','house',(166,215,164)),('Media Suite','media',(240,143,199))]
pos=[(480,63),(695,105),(803,233),(699,389),(480,432),(261,389),(157,233),(265,105)]
class SD:
 def __init__(self,img): self.d=ImageDraw.Draw(img)
 def xy(self,p): return tuple(v*S for v in p)
 def ellipse(self,b,**kw):
  if 'width' in kw:kw['width']*=S
  self.d.ellipse(self.xy(b),**kw)
 def rounded_rectangle(self,b,**kw):
  for k in ('width','radius'):
   if k in kw:kw[k]*=S
  self.d.rounded_rectangle(self.xy(b),**kw)
 def line(self,p,**kw):
  kw['width']=kw.get('width',1)*S;self.d.line([self.xy(x) for x in p],**kw)
 def multiline_text(self,p,s,**kw):
  if 'spacing' in kw:kw['spacing']*=S
  self.d.multiline_text(self.xy(p),s,**kw)
 def text(self,p,s,**kw):self.d.text(self.xy(p),s,**kw)
def sm(a,b,t):
 u=max(0,min(1,(t-a)/(b-a)));return u*u*(3-2*u)
def rgba(c,a):return (*c,int(max(0,min(255,a))))
def txt(d,s,xy,font,c):d.multiline_text(xy,s,font=fonts[font],fill=c,anchor='mm',align='center',spacing=2)
def glyph(d,k,x,y,c):
 def ln(p):d.line([(x+a,y+b) for a,b in p],fill=c,width=2,joint='curve')
 def el(a,b,r,fill=False):d.ellipse((x+a-r,y+b-r,x+a+r,y+b+r),outline=c,fill=c if fill else None,width=2)
 def rect(a,b,e,f,r=3):d.rounded_rectangle((x+a,y+b,x+e,y+f),radius=r,outline=c,width=2)
 if k=='hub':
  for a,b in [(0,-16),(15,-5),(9,14),(-10,14),(-15,-5)]:ln([(0,0),(a,b)]);el(a,b,3,True)
  el(0,0,6,True)
 elif k=='time':
  ln([(-18,10),(18,10)]);ln([(-16,-1),(-6,-1),(-6,-13),(6,-13),(6,2),(16,2)])
  for a in [-15,-5,5,15]:el(a,10,2,True)
 elif k=='scroll':
  rect(-13,-15,13,15);el(-13,-12,4);el(13,12,4);ln([(-7,-5),(7,-5)]);ln([(-7,1),(7,1)]);ln([(-7,7),(3,7)])
 elif k=='book':
  ln([(-17,-13),(-7,-12),(0,-7),(7,-12),(17,-13),(17,12),(7,10),(0,15),(-7,10),(-17,12),(-17,-13)]);ln([(0,-7),(0,15)]);ln([(-12,-5),(-5,-3)]);ln([(5,-3),(12,-5)])
 elif k=='gear':
  pts=[((17 if i%4 in (1,2) else 12)*math.cos(i*math.tau/32),(17 if i%4 in (1,2) else 12)*math.sin(i*math.tau/32)) for i in range(32)];ln(pts+[pts[0]]);el(0,0,6)
 elif k=='news':rect(-17,-13,17,13);rect(-12,-8,-3,1,1);ln([(2,-8),(12,-8)]);ln([(2,-3),(12,-3)]);ln([(-12,6),(12,6)])
 elif k=='house':ln([(-18,-1),(0,-16),(18,-1)]);rect(-13,-1,13,15,1);rect(-3,4,3,15,0);rect(6,2,10,6,0)
 elif k=='media':rect(-18,-12,18,12);ln([(-5,-6),(-5,6),(7,0),(-5,-6)]);ln([(-13,-12),(-8,-5)]);ln([(0,-12),(5,-5)]);ln([(13,-12),(18,-5)])
def paste(im,obj,center,size,a):
 if a<.002:return
 sz=max(1,int(size*S));o=obj.resize((sz,sz),Image.Resampling.LANCZOS)
 if a<.999:o.putalpha(o.getchannel('A').point(lambda v:int(v*a)))
 im.alpha_composite(o,(round(center[0]*S-sz/2),round(center[1]*S-sz/2)))
def draw(t, captions=True):
 im=base.copy();l=Image.new('RGBA',(WW,HH));d=SD(l); appear=sm(0,1.8,t);net=sm(2.2,4,t)*(1-sm(8.35,10.05,t));end=sm(12,13.55,t);cy=250-70*sm(8.4,10.3,t)
 for x,y,r,p,v in particles:
  x=(x*W+(4+v*12)*t)%W;y*=H;r=.4+r*1.1;d.ellipse((x-r,y-r,x+r,y+r),fill=(124,164,239,int((25+25*math.sin(p*6.28+t*.9))*(1-.65*end))))
 for k,col in enumerate([(99,95,250),(80,204,240),(174,89,243)]):
  rx=160+46*k+net*(150-20*k);ry=75+19*k+net*(58-8*k);pts=[(480+rx*math.cos(j*math.tau/120),250+ry*math.sin(j*math.tau/120)+12*math.sin(j*math.tau/60+t*.65+k)) for j in range(121)];d.line(pts,fill=rgba(col,(25+65*net)*appear*(1-end)),width=1)
  for j in range(3):
   x,y=pts[int(((t*.42+k*1.6+j*2.094)%math.tau)/math.tau*120)];d.ellipse((x-2,y-2,x+2,y+2),fill=rgba(col,170*appear*(1-end)))
 for i,((name,k,col),(px,py)) in enumerate(zip(apps,pos)):
  a=sm(2.9+i*.22,3.8+i*.22,t)*(1-sm(8.3,10,t));collapse=sm(8.4,10,t)
  if a<.005:continue
  sc=(.57+.43*sm(2.9+i*.22,4.05+i*.22,t))*(1-.84*collapse);x=480+(px-480)*sc;y=250+(py-250)*sc+4*math.sin(t*1.2+i*.9)*(1-collapse)
  d.line([(480,250),(x,y)],fill=rgba(col,65*a));p=(t*.66+i*.14)%1;xx=480+(x-480)*p;yy=250+(y-250)*p;d.ellipse((xx-2,yy-2,xx+2,yy+2),fill=rgba(col,235*a));r=32 if k=='hub' else 28
  d.ellipse((x-r-5,y-r-5,x+r+5,y+r+5),outline=rgba(col,38*a));d.rounded_rectangle((x-r,y-r,x+r,y+r),radius=15,fill=(16,25,48,int(240*a)),outline=rgba(col,190*a),width=2);glyph(d,k,x,y,rgba(col,240*a));txt(d,name,(x,y+r+19),'label',rgba((226,237,251),255*a))
 im=Image.alpha_composite(im,l);sz=(248-90*sm(2.2,4.2,t)+25*sm(8.3,10.1,t))*(.77+.23*appear);g=Image.new('RGBA',(WW,HH));gd=SD(g);r=sz*.54;gd.ellipse((480-r,cy-r,480+r,cy+r),fill=(35,110,245,int(90*appear*(1-end))));im=Image.alpha_composite(im,g.filter(ImageFilter.GaussianBlur(25*S)));paste(im,orb,(480,cy),sz*(1-.39*end),appear*(1-end));paste(im,blue,(480,cy),138,end)
 l=Image.new('RGBA',(WW,HH));d=SD(l);title=sm(9.1,10.45,t);sub=sm(10.3,11,t)*(1-sm(11.9,12.85,t));tag=sm(12.55,13.55,t)
 if title>.001:txt(d,'O P S I S',(480,303),'title',rgba((240,247,255),255*title))
 if captions and sub>.001:txt(d,'Desktop Customization',(480,356),'sub',rgba((190,213,239),255*sub))
 if captions and tag>.001:txt(d,'Ένα οικοσύστημα, αμέτρητες δυνατότητες',(480,356),'tag',rgba((198,226,253),255*tag))
 a=sm(12.1,13.45,t)
 if a>.001:
  paste(im,gs,(808,493),44,a);d.text((838,493),'GS App Labs',font=fonts['label'],anchor='lm',fill=rgba((205,224,245),255*a))
 return Image.alpha_composite(im,l).convert('RGB')
def export_icons(out):
 out.mkdir(parents=True,exist_ok=True)
 for name,key,col in apps:
  im=Image.new('RGBA',(128,128));d=SD(im)
  d.rounded_rectangle((4,4,60,60),radius=15,fill=(16,25,48,240),outline=rgba(col,190),width=2)
  glyph(d,key,32,32,rgba(col,240));im.save(out/f'{key}.png')
 orb.save(out/'spectrum-blue.png');blue.save(out/'opsis-official.png');gs.save(out/'gs-app-labs.png')
 blue.save(out/'opsis-official.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])

def main():
 import subprocess
 parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=P/'intro.webm');parser.add_argument('--clean',action='store_true',help='Omit baked subtitle and slogan for localized overlays');parser.add_argument('--icons',type=Path);parser.add_argument('--plate',type=Path);args=parser.parse_args()
 if args.icons: export_icons(args.icons)
 if args.plate: base.crop((360,650,1560,780)).convert('RGB').save(args.plate)
 if args.icons or args.plate:return
 cmd=['ffmpeg','-hide_banner','-loglevel','error','-y','-f','rawvideo','-pix_fmt','rgb24','-s',f'{WW}x{HH}','-r',str(FPS),'-i','-','-an','-c:v','libvpx-vp9','-deadline','realtime','-cpu-used','6','-row-mt','1','-threads','4','-b:v','0','-crf','19','-pix_fmt','yuv420p',str(args.output)]
 proc=subprocess.Popen(cmd,stdin=subprocess.PIPE)
 try:
  for i in range(FPS*15):proc.stdin.write(draw(i/FPS,not args.clean).tobytes())
 finally:proc.stdin.close()
 if proc.wait()!=0:raise RuntimeError('ffmpeg failed')
if __name__=='__main__':main()
