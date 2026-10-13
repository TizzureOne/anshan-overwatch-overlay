"""Build-time only: download official hero portraits and bake circular PNGs.

Run with Python + Pillow. The shipped application never downloads hero assets.
"""
import concurrent.futures
import hashlib
import html
import io
import json
import pathlib
import re
import subprocess
from datetime import datetime, timezone
from PIL import Image, ImageDraw, ImageOps

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "src/AnshanOverlay/Assets/Heroes"
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = "https://overwatch.blizzard.com/en-us/heroes/"

def get(url):
    # Windows curl uses the system TLS backend, including the system proxy trust.
    return subprocess.run(["curl.exe", "-fL", "--retry", "2", "--max-time", "40",
                           "-sS", url], check=True, capture_output=True).stdout

def cards(text):
    result = {}
    for card in re.findall(r'<a class="hero-card".*?</a>', text):
        key = re.search(r' id="([^"]+)"', card)[1]
        result[key] = {
            "id": key,
            "englishName": html.unescape(re.search(r'<h2[^>]*>(.*?)</h2>', card)[1]),
            "role": re.search(r'data-role="([^"]+)"', card)[1],
            "sourceUrl": html.unescape(re.search(r'<blz-image[^>]* src="([^"]+)"', card)[1]),
        }
    return result

# Established mainland names and common search aliases. New entries fall back to
# the official Traditional Chinese label until a mainland label is verified.
NAMES = {
 "ana": ("安娜", "an anna an na"), "ashe": ("艾什", "as ai shi"),
 "baptiste": ("巴蒂斯特", "bdst ba di si te"), "bastion": ("堡垒", "bl bao lei"),
 "brigitte": ("布丽吉塔", "bljt xiaochui 小锤 bu li ji ta"),
 "cassidy": ("卡西迪", "kxd mkl 麦克雷 ka xi di"), "dva": ("D.Va", "dva dva 宋哈娜"),
 "doomfist": ("末日铁拳", "mrtq 铁拳 mo ri tie quan"), "echo": ("回声", "hs hui sheng"),
 "freja": ("弗蕾娅", "fly fu lei ya"), "genji": ("源氏", "ys yuan shi"),
 "hanzo": ("半藏", "bz ban zang"), "hazard": ("骇灾", "hz hai zai"),
 "illari": ("伊拉锐", "ylr yi la rui"), "junker-queen": ("渣客女王", "zknw 女王 zha ke nv wang"),
 "junkrat": ("狂鼠", "ks kuang shu"), "juno": ("朱诺", "zn zhu nuo"),
 "kiriko": ("雾子", "wz wu zi"), "lifeweaver": ("生命之梭", "smzs 织命 sheng ming zhi suo"),
 "lucio": ("卢西奥", "lxa DJ lu xi ao"), "mauga": ("毛加", "mj mao jia"),
 "mei": ("美", "小美 xm mei"), "mercy": ("天使", "ts tian shi"),
 "moira": ("莫伊拉", "myl mo yi la"), "orisa": ("奥丽莎", "als ao li sha"),
 "pharah": ("法老之鹰", "flzy 法鸡 fa lao zhi ying"), "ramattra": ("拉玛刹", "lmc la ma cha"),
 "reaper": ("死神", "ss si shen"), "reinhardt": ("莱因哈特", "lyht 大锤 lai yin ha te"),
 "roadhog": ("路霸", "lb 猪 lu ba"), "sigma": ("西格玛", "xgm xi ge ma"),
 "sojourn": ("索杰恩", "sje suo jie en"), "soldier-76": ("士兵：76", "sb76 76 shi bing"),
 "sombra": ("黑影", "hy hei ying"), "symmetra": ("秩序之光", "zxzg 三妹 zhi xu zhi guang"),
 "torbjorn": ("托比昂", "tba tuo bi ang"), "tracer": ("猎空", "lk lie kong"),
 "venture": ("探奇", "tq tan qi"), "widowmaker": ("黑百合", "hbh hei bai he"),
 "winston": ("温斯顿", "wsd 猩猩 wen si dun"), "wrecking-ball": ("破坏球", "phq 仓鼠 po huai qiu"),
 "wuyang": ("无漾", "wy wu yang"), "zarya": ("查莉娅", "cly 毛妹 cha li ya"),
 "zenyatta": ("禅雅塔", "cyt 和尚 chan ya ta"), "anran": ("安燃", "ar an ran"),
 "dmon": ("D.Mon", "dmon"), "domina": ("金驭", "jy jin yu"),
 "doctrine": ("血律", "xl xue lv xuelv"),
 "emre": ("埃姆雷", "aml ai mu lei"), "jetpack-cat": ("飞天猫", "ftm fei tian mao"),
 "mizuki": ("瑞希", "rx rui xi"), "shion": ("死怨", "sy si yuan"),
 "sierra": ("西拉", "xl xi la"), "vendetta": ("斩仇", "zc zhan chou"),
}

def main():
    en = get(SOURCE).decode("utf-8")
    tw = get("https://overwatch.blizzard.com/zh-tw/heroes/").decode("utf-8")
    (ROOT / "tools/heroes-en.html").write_text(en, encoding="utf-8")
    heroes, translated = cards(en), cards(tw)
    cn_url = "https://ld5.res.netease.com/pc/zt/20241115153332/static/heroConfigs.json"
    cn_data = json.loads(get(cn_url))["heroConfigs"]
    cn = {("jetpack-cat" if h["id"] == "jetpackcat" else h["id"]): h["name"] for h in cn_data}
    if len(heroes) < 40 or set(heroes) != set(translated):
        raise RuntimeError("Official hero lists incomplete or inconsistent")
    if set(heroes) != set(cn):
        raise RuntimeError("Mainland and global official hero lists are inconsistent")

    def download(hero):
        key = hero["id"]
        cache = ROOT / "tools/originals" / f"{key}.png"
        cache.parent.mkdir(parents=True, exist_ok=True)
        original = cache.read_bytes() if cache.exists() else get(hero["sourceUrl"])
        cache.write_bytes(original)
        source = Image.open(io.BytesIO(original)).convert("RGBA")
        # Supersample the mask for clean small-size edges, once at build time.
        circle = ImageOps.fit(source, (512, 512), centering=(0.5, 0.5))
        mask = Image.new("L", (512, 512))
        ImageDraw.Draw(mask).ellipse((1, 1, 510, 510), fill=255)
        from PIL import ImageChops
        circle.putalpha(ImageChops.multiply(circle.getchannel("A"), mask))
        circle = circle.resize((256, 256), Image.Resampling.LANCZOS)
        path = OUT / f"{key}.png"
        circle.save(path, optimize=True)
        name, aliases = NAMES.get(key, (translated[key]["englishName"], ""))
        hero.update(name=cn[key], aliases=(aliases + " " + name).strip(), image=f"{key}.png",
                    originalSha256=hashlib.sha256(original).hexdigest(),
                    sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                    originalSize=list(source.size))
        return hero

    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        complete = list(pool.map(download, heroes.values()))
    manifest = {"source": SOURCE, "nameSource": cn_url, "retrievedAt": datetime.now(timezone.utc).isoformat(),
                "count": len(complete), "heroes": complete}
    (OUT.parent / "heroes.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Downloaded and verified {len(complete)} / {len(heroes)} official portraits")
    print(f"PNG total: {sum(p.stat().st_size for p in OUT.glob('*.png')) / 1024 / 1024:.2f} MiB")

if __name__ == "__main__":
    main()
