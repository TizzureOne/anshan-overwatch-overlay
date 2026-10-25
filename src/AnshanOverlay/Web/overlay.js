"use strict";
const svg = document.getElementById("board");
const ns = "http://www.w3.org/2000/svg";
const nodes = new Map();
let names = {}, nextScene, framePending = false, maskCounter = 0;
function make(tag, attrs = {}) {
  const node = document.createElementNS(ns, tag);
  set(node, attrs); return node;
}
function set(node, attrs) { for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, String(value)); }
function heroNodes() {
  const group = make("g"), base = make("circle", {fill:"#15202C",stroke:"none"});
  const image = make("image"), ring = make("circle", {fill:"none","stroke-width":3});
  const labelBg = make("rect", {fill:"#15202C",rx:4});
  const text = make("text", {"text-anchor":"middle","dominant-baseline":"hanging"});
  group.append(base, image, ring, labelBg, text);
  return {group, base, image, ring, labelBg, text};
}
function lineNodes() {
  const group = make("g");
  const line = make("line", {"stroke-linecap":"round"});
  const head = make("polygon", {stroke:"none"});
  group.append(line, head); return {group, line, head};
}
function shapeNodes(kind) {
  const group=make("g"), tag=kind==="circle"?"ellipse":kind==="rectangle"?"rect":"polyline";
  const shape=make(tag,{fill:"none","stroke-linecap":"round","stroke-linejoin":"round"});
  group.append(shape);return {group,shape};
}
function render(scene) {
  if (scene.heroCatalog) names = scene.heroCatalog;
  set(svg, {viewBox:`0 0 ${scene.width} ${scene.height}`});
  svg.style.opacity = scene.visible ? "1" : "0";
  const keep = new Set();
  for (const item of scene.items) {
    keep.add(item.id);
    let n = nodes.get(item.id);
    if (n && n.kind !== item.kind) { n.group.remove(); nodes.delete(item.id); n = null; }
    if (!n) { n = item.kind === "hero" ? heroNodes() : item.kind === "line" ? lineNodes() : shapeNodes(item.kind); n.kind = item.kind; nodes.set(item.id, n); }
    // Append also restores the scene's stacking order after undo/load.
    svg.append(n.group);
    set(n.group, {opacity:item.opacity ?? 1});
    if (item.erasures?.length) {
      if (!n.mask) { n.maskId = "erase-" + (++maskCounter); n.mask = make("mask", {id:n.maskId,maskUnits:"userSpaceOnUse",maskContentUnits:"userSpaceOnUse",style:"mask-type:luminance"}); n.defs=make("defs");n.defs.append(n.mask);n.group.append(n.defs); }
      set(n.mask,{x:0,y:0,width:scene.width,height:scene.height});
      n.mask.replaceChildren(make("rect",{x:0,y:0,width:scene.width,height:scene.height,fill:"white"}));
      for (const e of item.erasures) n.mask.append(Math.hypot(e.x2-e.x,e.y2-e.y)<.01 ?
        make("circle",{cx:e.x,cy:e.y,r:e.radius,fill:"black"}) :
        make("line",{x1:e.x,y1:e.y,x2:e.x2,y2:e.y2,stroke:"black","stroke-width":e.radius*2,"stroke-linecap":"round"}));
      set(n.group,{mask:`url(#${n.maskId})`});
    } else n.group.removeAttribute("mask");
    if (item.kind === "hero") {
      const r = item.size / 2;
      set(n.base, {cx:item.x,cy:item.y,r}); set(n.ring,{cx:item.x,cy:item.y,r,stroke:item.team==="enemy"?"#F05265":"#39A9FF"});
      const portrait = `/heroes/${item.heroId}.png` + (scene.heroRevision ? `?v=${encodeURIComponent(scene.heroRevision)}` : "");
      set(n.image, {x:item.x-r,y:item.y-r,width:item.size,height:item.size,href:portrait});
      n.text.style.display = n.labelBg.style.display = scene.showHeroNames ? "" : "none";
      if (scene.showHeroNames) {
        const size = Math.min(24, Math.max(12, item.size * 0.19));
        n.text.textContent = names[item.heroId] || item.heroId;
        set(n.text,{x:item.x,y:item.y+r+9,"font-size":size});
        const w = n.text.getComputedTextLength();
        set(n.labelBg,{x:item.x-w/2-7,y:item.y+r+5,width:w+14,height:size*1.4+4});
      }
    } else if(item.kind==="circle" || item.kind==="path" || item.kind==="rectangle") {
      const attrs=item.kind==="circle"?{cx:(item.x+item.x2)/2,cy:(item.y+item.y2)/2,rx:Math.abs(item.x2-item.x)/2,ry:Math.abs(item.y2-item.y)/2}:item.kind==="rectangle"?{x:Math.min(item.x,item.x2),y:Math.min(item.y,item.y2),width:Math.abs(item.x2-item.x),height:Math.abs(item.y2-item.y)}:{points:item.points.map(p=>`${p.x},${p.y}`).join(" ")};
      attrs["stroke-dasharray"]=item.dashed?`${item.width*3} ${item.width*2}`:"none";
      set(n.shape,{...attrs,"stroke-width":item.width,stroke:item.color});
    } else {
      const dx = item.x2-item.x, dy=item.y2-item.y, length=Math.hypot(dx,dy);
      n.group.style.display = length < 1 ? "none" : "";
      if (length < 1) continue;
      const ux=dx/length, uy=dy/length, head=Math.min(length*0.7,Math.max(16,item.width*3.5));
      const x2=item.arrow ? item.x2-ux*head*0.75 : item.x2;
      const y2=item.arrow ? item.y2-uy*head*0.75 : item.y2;
      const common={x1:item.x,y1:item.y,x2,y2,"stroke-dasharray":item.dashed?`${item.width*3} ${item.width*2}`:"none"};
      set(n.line,{...common,stroke:item.color,"stroke-width":item.width});
      n.head.style.display=item.arrow?"":"none";
      if(item.arrow) set(n.head,{fill:item.color,points:`${item.x2},${item.y2} ${item.x2-ux*head-uy*head*.48},${item.y2-uy*head+ux*head*.48} ${item.x2-ux*head+uy*head*.48},${item.y2-uy*head-ux*head*.48}`});
    }
  }
  for(const [id,n] of nodes) if(!keep.has(id)){n.group.remove();nodes.delete(id);}
}
function connect() {
  const ws = new WebSocket(`ws://${location.host}/ws`);
  ws.onmessage = event => {
    nextScene = JSON.parse(event.data);
    if (!framePending) {
      framePending = true;
      requestAnimationFrame(() => { framePending = false; if(ws.readyState===WebSocket.OPEN) render(nextScene); });
    }
  };
  ws.onclose = () => { svg.style.opacity="0"; setTimeout(connect,1500); };
  ws.onerror = () => ws.close();
}
fetch("/catalog").then(r=>r.json()).then(data=>{names=data;connect();}).catch(()=>setTimeout(()=>location.reload(),2000));
