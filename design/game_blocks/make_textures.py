"""Original, deterministic PBR trim atlases. Python 3 + numpy + Pillow.

No stock or game textures. Panels share trim UVs deliberately for a single draw
material per model. Labels, gauges and screens are raster details, not geometry.
"""
from pathlib import Path
import json
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parent
SIZE, TILE = 2048, 512
FONT = '/usr/share/fonts/liberation-sans-fonts/LiberationSans-Bold.ttf'
MONO = '/usr/share/fonts/liberation-mono-fonts/LiberationMono-Regular.ttf'
TILES = ['paint', 'secondary', 'steel', 'rubber', 'ivory', 'rust', 'screen',
         'keypad', 'title', 'service', 'gauge', 'legend', 'hazard', 'vents',
         'tiers', 'lamps']


def font(size, mono=False):
    return ImageFont.truetype(MONO if mono else FONT, size)


def noise(rng, small, scale=1):
    a = rng.random((small, small)) * 255
    return np.asarray(Image.fromarray(a.astype('uint8')).resize((TILE, TILE), Image.Resampling.BICUBIC), dtype=float) / 255 * scale


def surface(base, seed, kind='paint'):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[:TILE, :TILE]
    edge = np.minimum.reduce([xx, yy, TILE-1-xx, TILE-1-yy]) / TILE
    cloud, grain = noise(rng, 11), noise(rng, 180)
    fine = rng.normal(0, 1.4, (TILE, TILE))
    streak = noise(rng, 35)
    color = np.array(base)[None,None,:] * (.86 + .23*cloud[...,None]) + fine[...,None]
    rough = np.clip(.73 + .13*(cloud-.5), 0, 1)
    metal = np.ones((TILE,TILE)) * .13
    height = grain*.005
    if kind in ('paint', 'steel', 'rust'):
        chips = (edge < .011 + .055*(cloud-.4)) & (grain > .41)
        pits = ((cloud>.91) & (grain>.81))
        rust = chips | pits
        rust_color = np.stack([86+33*grain, 47+17*grain, 25+12*grain], axis=-1)
        color[rust] = rust_color[rust]
        exposed = chips & (grain > .65) & (edge < .013)
        color[exposed] = np.stack([112+40*grain, 115+35*grain, 109+32*grain],axis=-1)[exposed]
        metal[exposed] = .85
        rough[rust] = .93
        height[rust] -= .10
        # Accumulated dirt below seams; no uniform orange noise covering the paint.
        grime = (np.exp(-edge*40) * .16 + np.clip(streak-.5,0,.5)*.13)
        color *= (1-grime[...,None])
        if kind == 'steel':
            metal[:] = .82
            rough[:] = .49 + grain*.17
        if kind == 'rust':
            color = np.stack([84+33*cloud, 49+14*cloud, 30+8*cloud],axis=-1) + fine[...,None]
            metal[:] = .30
            rough[:] = .86
    if kind == 'rubber':
        metal[:] = 0
        rough[:] = .88
    img = Image.fromarray(np.clip(color,0,255).astype('uint8'))
    d = ImageDraw.Draw(img)
    if kind in ('paint','steel'):
        # Small abrasions, mostly at the perimeter and lower contact zone.
        for _ in range(90):
            x, y = rng.integers(10,502,2)
            if 65<x<447 and 65<y<430 and rng.random()>.15:
                continue
            length = int(rng.integers(3,29))
            d.line((int(x),int(y),int(x+length),int(y+rng.integers(-3,4))), fill=(103,107,96),width=1)
    return img, metal, rough, height


def make(family, seed, body, second, title, service):
    atlas = Image.new('RGB',(SIZE,SIZE))
    metal = np.zeros((SIZE,SIZE),float)
    rough = np.ones((SIZE,SIZE),float)*.8
    height = np.zeros((SIZE,SIZE),float)
    emit = Image.new('RGB',(SIZE,SIZE))
    colors = [body, second, (71,76,74), (24,28,26), (181,175,147),
              (107,61,36), (17,30,25), (37,42,36), body, (45,49,44),
              (197,187,154), (51,57,49), (165,133,55), (29,33,30),
              (32,39,35), (29,33,30)]
    for index,name in enumerate(TILES):
        kind = 'steel' if name=='steel' else 'rust' if name=='rust' else 'rubber' if name in ('rubber','screen','keypad','lamps','vents') else 'paint'
        img, m, r, h = surface(colors[index],seed+index,kind)
        d = ImageDraw.Draw(img)
        e = Image.new('RGB',(TILE,TILE))
        ed = ImageDraw.Draw(e)
        cream, ink = (205,199,168), (38,43,37)
        if name == 'screen':
            m[:] = 0; r[:] = .58
            for y in range(14,500,4):
                d.line((12,y,500,y),fill=(21,40,30))
            green=(136,166,112)
            d.text((30,27),'NC / SUPPLY LINK',font=font(33,True),fill=green)
            d.line((30,77,480,77), fill=green,width=2)
            rows = [('NETWORK','LOCAL'),('STORAGE','ONLINE'),('PRODUCTION','READY')]
            for i,(a,b) in enumerate(rows):
                y=111+i*72
                d.text((31,y),a,font=font(24,True),fill=(93,121,81))
                d.text((315,y),b,font=font(22,True),fill=green)
                d.line((32,y+44,473,y+44),fill=(43,67,44))
            for i in range(17):
                d.rectangle((32+i*26,363,48+i*26,390),fill=green if i<13 else (33,55,39))
            d.text((31,432),'ACCESS / AUTHORIZED',font=font(25,True),fill=green)
            ea = np.asarray(img).copy()
            ea[np.max(ea,axis=-1)<75]=0
            e=Image.fromarray((ea*.34).astype('uint8'))
        elif name=='keypad':
            for row in range(4):
                for col in range(3):
                    x,y=23+col*160,20+row*122
                    d.rounded_rectangle((x,y,x+141,y+100),radius=8,fill=(79,81,68),outline=(117,116,94),width=3)
                    k=str(row*3+col+1) if row<3 else ['CLR','0','ENT'][col]
                    d.text((x+70,y+49),k,anchor='mm',font=font(40,True),fill=cream)
        elif name=='title':
            d.rectangle((17,120,495,386),outline=cream,width=4)
            d.text((37,143),'NEARBYCRAFT',font=font(28),fill=cream)
            # Fits the narrow, aspect-matched 44–50px nameplate UV strips.
            d.text((256,256),title,font=font(40),fill=cream,anchor='mm')
            d.text((37,298),'FIELD SYSTEMS  /  SERIES 07',font=font(22,True),fill=cream)
        elif name=='service':
            d.rectangle((24,40,488,472),outline=cream,width=4)
            d.text((45,65),'CAUTION',font=font(54),fill=(193,152,61))
            d.line((45,139,465,139),fill=cream,width=3)
            for i,s in enumerate(service):
                d.text((45,175+i*51),s,font=font(26,True),fill=cream)
            d.text((45,410),'NC-07 / 24V DC',font=font(22,True),fill=cream)
        elif name=='gauge':
            # One analog instrument; orientation is varied on the mesh.
            d.ellipse((30,30,482,482),fill=(190,181,145),outline=ink,width=12)
            for i in range(31):
                a=math.radians(150+i*8)
                r0=165 if i%5==0 else 180
                d.line((256+math.cos(a)*r0,270+math.sin(a)*r0,256+math.cos(a)*204,270+math.sin(a)*204),fill=ink,width=4 if i%5==0 else 2)
            d.arc((60,72,452,464),start=324,end=384,fill=(125,57,39),width=12)
            d.text((256,344),'LOAD %',font=font(38,True),fill=ink,anchor='mm')
            d.line((253,274,347,119),fill=(114,43,26),width=8)
            d.ellipse((240,253,272,285),fill=ink)
            d.text((255,405),'0     100',font=font(25,True),fill=ink,anchor='mm')
            r[:]=.5
        elif name=='legend':
            for i,s in enumerate(['POWER','MANUAL','AUTO','RESET','01 / FIELD','02 / RESERVE','LOCK / RELEASE','SUPPLY / RETURN']):
                y=i*64
                d.rectangle((8,y+5,503,y+57),fill=(48,53,47),outline=(115,119,98),width=1)
                d.text((256,y+31),s,font=font(29,True),fill=cream,anchor='mm')
        elif name=='hazard':
            for i in range(-512,1024,130):
                d.polygon([(i,0),(i+60,0),(i+572,512),(i+512,512)],fill=(37,40,33))
            # Paint eroded at contacts.
            rng=np.random.default_rng(seed)
            for _ in range(230):
                x,y=rng.integers(0,512,2); w=int(rng.integers(1,12))
                d.line((int(x),int(y),int(x+w),int(y+1)),fill=(99,93,66),width=2)
        elif name=='vents':
            for y in range(25,500,45):
                d.rounded_rectangle((24,y,488,y+26),radius=8,fill=(11,15,13))
                d.line((30,y+28,482,y+28),fill=(76,79,64),width=4)
        elif name=='tiers':
            for i,(color,cap) in enumerate([((90,156,153),'08'),((123,154,86),'16'),((100,137,167),'32'),((151,120,160),'64')]):
                y=i*128
                d.rectangle((12,y+10,500,y+118),fill=(32,39,35),outline=color,width=5)
                d.text((34,y+64),f'T{i+1} / {cap} NODES',font=font(43,True),fill=color,anchor='lm')
        elif name=='lamps':
            for i,col in enumerate([(132,170,101),(211,150,62),(165,59,38),(87,141,157)]):
                x=i*128
                d.rectangle((x+2,2,x+126,510), fill=col)
                ed.rectangle((x+2,2,x+126,510),fill=tuple(int(v*.5) for v in col))
            r[:]=.36; m[:]=.1
        x,y=(index%4)*TILE,(index//4)*TILE
        atlas.paste(img,(x,y)); emit.paste(e,(x,y))
        metal[y:y+TILE,x:x+TILE]=m
        rough[y:y+TILE,x:x+TILE]=r
        height[y:y+TILE,x:x+TILE]=h
    out=ROOT/'textures'; out.mkdir(exist_ok=True)
    atlas.save(out/f'{family}_BaseColor.png')
    Image.fromarray(np.uint8(rough*255)).save(out/f'{family}_Roughness.png')
    Image.fromarray(np.uint8(metal*255)).save(out/f'{family}_Metallic.png')
    # Unity Standard: metallic in R; smoothness = 1 - roughness in A.
    packed=np.zeros((SIZE,SIZE,4),dtype='uint8')
    packed[:,:,:3]=np.uint8(metal[:,:,None]*255)
    packed[:,:,3]=np.uint8((1-rough)*255)
    Image.fromarray(packed).save(out/f'{family}_MetallicSmoothness.png')
    gy,gx=np.gradient(height)
    normal=np.stack([-gx*2,gy*2,np.ones_like(gx)],axis=-1)
    normal/=np.linalg.norm(normal,axis=-1,keepdims=True)
    Image.fromarray(np.uint8((normal*.5+.5)*255)).save(out/f'{family}_Normal.png')
    emit.save(out/f'{family}_Emission.png')


if __name__=='__main__':
    make('console',47,(80,111,100),(54,74,67),'STORAGE', ['ISOLATE POWER','BEFORE SERVICE','KEEP VENTS CLEAR','SUPPLY ACCESS'])
    make('workshop',89,(152,128,66),(81,87,66),'WORKSHOP',['ISOLATE POWER','CHECK ALL LINES','MANUAL OVERRIDE','AUTHORIZED ONLY'])
    make('locker',139,(80,101,108),(104,112,81),'LOADOUT',['ASSIGNED GEAR','RETURN AFTER USE','KEEP DOORS SHUT','NETWORK ACCESS'])
    (ROOT/'textures'/'atlas_layout.json').write_text(json.dumps({n: {'x':(i%4)*TILE,'y':(i//4)*TILE,'width':TILE,'height':TILE} for i,n in enumerate(TILES)},indent=2)+'\n')
    print('Created three original 2048px PBR atlases.')
