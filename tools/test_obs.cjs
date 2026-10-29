// npm install linkedom --prefix artifacts/qa/dom --ignore-scripts --no-audit --no-fund
const fs = require('node:fs');
const assert = require('node:assert/strict');
const { parseHTML } = require('../artifacts/qa/dom/node_modules/linkedom');
const { document, window } = parseHTML('<svg id="board"></svg>');
window.Element.prototype.getComputedTextLength = () => 40;
const code = fs.readFileSync('src/AnshanOverlay/Web/overlay.js', 'utf8').split('fetch("/catalog")')[0];
const render = new Function('document', code + '\nreturn render;')(document);
const svg = document.getElementById('board');
let passed = 0;
function check(value, message) { assert.ok(value, message); passed++; }
function checkTemporaryInk() {
    const permanent = { id: 'permanent', kind: 'path', color: '#39A9FF', width: 5, points: [{ x: 10, y: 10 }, { x: 90, y: 10 }] };
    const temporary = { ...permanent, id: 'temporary', color: '#FFD166', points: [{ x: 10, y: 80 }, { x: 90, y: 80 }] };
    const state = { width: 800, height: 600, visible: true, items: [permanent, temporary] };
    render(state);
    const permanentNode = svg.children[0];
    check(svg.children.length === 2 && svg.children[1].querySelector('polyline').getAttribute('stroke') === '#FFD166', 'temporary brush uses the same visible OBS path rendering');
    render({ ...state, items: [permanent, { ...temporary, points: [...temporary.points, { x: 120, y: 100 }] }] });
    check(svg.children[1].querySelector('polyline').getAttribute('points') === '10,80 90,80 120,100', 'temporary live preview updates the existing OBS stroke');
    render({ ...state, items: [permanent] });
    check(svg.children.length === 1 && svg.children[0] === permanentNode && permanentNode.querySelector('polyline').getAttribute('points') === '10,10 90,10', 'expiration removes only temporary ink and preserves the permanent node');
    render({ ...state, visible: false });
    check(svg.style.opacity === '0', 'temporary ink follows scene visibility');
    render({ ...state, items: [permanent] });
    check(svg.style.opacity === '1' && svg.children.length === 1, 'latest snapshot after reconnect cannot restore expired ink');
}
if (process.argv.includes('--temporary-ink-only')) {
    checkTemporaryInk();
    console.log(JSON.stringify({ passed, domOnly: true, scope: 'temporary ink' }));
    process.exit(0);
}
const shapes = ['path', 'line', 'line', 'circle', 'rectangle'].map((kind, n) => ({
    id: 'shape' + n, kind, x: 300, y: 200, x2: 100, y2: 100, width: 4, color: '#39A9FF',
    arrow: n === 2, dashed: true, points: [{ x: 300, y: 200 }, { x: 200, y: 170 }, { x: 100, y: 100 }]
}));
const hero = { id: 'hero', kind: 'hero', x: 400, y: 300, size: 80, team: 'ally', heroId: 'ana' };
const scene = { width: 1920, height: 1080, visible: true, showHeroNames: true, items: [...shapes, hero] };
render(scene);
check(svg.children.length === 6, 'all shapes and hero emitted');
for (const group of [...svg.children].slice(0, 5)) {
    check(group.children[0].getAttribute('stroke-dasharray') === '12 8', 'shape has shared dash');
}
const ellipse = svg.children[3].children[0], rect = svg.children[4].children[0];
check(ellipse.localName === 'ellipse' && ellipse.getAttribute('rx') === '100' && ellipse.getAttribute('ry') === '50', 'independent ellipse radii');
check(rect.getAttribute('x') === '100' && rect.getAttribute('y') === '100' && rect.getAttribute('width') === '200' && rect.getAttribute('height') === '100', 'reverse rectangle normalized');
check(svg.children[5].querySelector('text').style.display !== 'none', 'global names enabled');
check(svg.children[5].children[2].getAttribute('stroke') === '#39A9FF', 'ally blue');
render({ ...scene, heroCatalog: { ana: '安娜（更新）' }, heroRevision: 'revision 1' });
check(svg.children[5].querySelector('text').textContent === '安娜（更新）', 'live catalog updates existing hero labels');
check(svg.children[5].querySelector('image').getAttribute('href') === '/heroes/ana.png?v=revision%201', 'live portrait revisions invalidate the browser image cache');
render({ ...scene, showHeroNames: false, items: [...shapes.map(s => ({ ...s, dashed: false })), { ...hero, team: 'enemy' }] });
for (const group of [...svg.children].slice(0, 5)) check(group.children[0].getAttribute('stroke-dasharray') === 'none', 'dash can reset');
check(svg.children[5].querySelector('text').style.display === 'none', 'global names disabled');
check(svg.children[5].children[2].getAttribute('stroke') === '#F05265', 'enemy red');
render({ ...scene, items: [{ ...shapes[0], kind: 'rectangle' }] });
check(svg.children.length === 1 && svg.children[0].children[0].localName === 'rect', 'load replaces same-id shape of different kind');
check(!svg.querySelector('[stroke="#101820"]'), 'no black outline on marks');
render({ ...scene, items: [{ ...shapes[0], opacity: .35, erasures: [{ x: 200, y: 160, x2: 210, y2: 170, radius: 12 }] }] });
check(svg.children[0].getAttribute('opacity') === '0.35', 'item opacity synchronized');
check(svg.children[0].getAttribute('mask').startsWith('url(#erase-'), 'partial erasure mask assigned');
const mask = svg.querySelector('mask');
check(mask.getAttribute('maskUnits') === 'userSpaceOnUse' && mask.querySelector('line').getAttribute('stroke-width') === '24', 'local eraser uses canvas coordinates and diameter');
render({ ...scene, items: [shapes[0]] });
check(!svg.children[0].hasAttribute('mask'), 'undo removes erasure mask');
render({ ...scene, visible: false, items: [] });
check(svg.children.length === 0 && svg.style.opacity === '0', 'erased items removed and visibility synced');
const label = { id: 'label', kind: 'text', x: 30, y: 40, text: '先占高台 <B>', fontSize: 28, color: '#FFD166', opacity: .6, label: { width: 200, height: 50, baseline: 35 } };
const number = { ...label, id: 'number', kind: 'number', text: '12', label: { width: 54, height: 54, baseline: 36 } };
render({ ...scene, items: [label, number] });
check(svg.children.length === 2 && svg.children[0].querySelector('text').textContent === label.text && !svg.querySelector('B'), 'text is literal and cannot create markup');
check(svg.children[0].getAttribute('opacity') === '0.6' && svg.children[0].querySelector('text').style.fill === '#FFD166', 'text color and opacity are synchronized');
check(svg.children[0].querySelector('rect').getAttribute('width') === '200' && svg.children[0].querySelector('text').getAttribute('y') === '75', 'desktop metrics control OBS label bounds and baseline');
check(svg.children[1].querySelector('rect').getAttribute('rx') === '27' && svg.children[1].querySelector('text').textContent === '12', 'number badge has circular background and number');
render({ ...scene, showHeroNames: false, items: [{ ...label, text: '改走左路', x: 120, fontSize: 40 }] });
check(svg.children.length === 1 && svg.children[0].querySelector('text').textContent === '改走左路' && svg.children[0].querySelector('text').getAttribute('font-size') === '40' && svg.children[0].querySelector('rect').getAttribute('x') === '120', 'editing moving and deleting labels updates existing nodes independently of hero names');
render({ ...scene, items: [number, label] });
check(svg.children[0].querySelector('text').textContent === '12' && svg.children[1].querySelector('text').textContent === label.text, 'undo restores label content and stacking order');
render({ ...scene, visible: false, items: [label] });
check(svg.style.opacity === '0', 'label visibility follows the scene');
render({ ...scene, items: [{ ...number, id: label.id }] });
check(svg.children.length === 1 && svg.children[0].querySelector('text').textContent === '12', 'loading a same-id number replaces a text label');
checkTemporaryInk();
const fixturePath = process.argv[2];
if (fixturePath && !fixturePath.startsWith('--')) {
    const { dashed, tail, expired } = JSON.parse(fs.readFileSync(fixturePath, 'utf8'));
    render(dashed);
    const path = svg.children[0].querySelector('polyline');
    check(path.getAttribute('stroke-dasharray') === '12 8' && path.getAttribute('stroke') === '#39A9FF', 'hero dashed trail uses the configured pattern and team color');
    check(path.getAttribute('points') === dashed.items[0].points.map(p => `${p.x},${p.y}`).join(' '), 'OBS follows the exact desktop trail coordinates');
    check(svg.children[1].querySelector('image') !== null, 'trail is stacked behind the hero portrait');
    render(tail);
    const paths = tail.items.filter(i => i.isHeroTrail), bands = paths[0].trailBands;
    const bandNodes = [...svg.children[0].children].slice(1);
    check(paths.length === 1 && bands.length <= 16 && bandNodes.length === bands.length && svg.children.length === tail.items.length, 'tail uses one scene item and at most sixteen continuous band nodes');
    check(bands.every((band, n) => +bandNodes[n].getAttribute('stroke-width') === band.width && +bandNodes[n].getAttribute('opacity') === band.opacity && bandNodes[n].getAttribute('points') === band.points.map(p => `${p.x},${p.y}`).join(' ')), 'OBS reproduces desktop tail geometry width and opacity for every band');
    check(bands[0].width < bands.at(-1).width && bands[0].opacity < bands.at(-1).opacity, 'tail narrows and fades toward the older end');
    check(bandNodes.every(n => n.getAttribute('stroke-linecap') === 'butt'), 'tail joins do not overlap round caps and form bright dots');
    const heroNode = svg.children[paths.length];
    render(expired);
    check(svg.children.length === expired.items.length && svg.children[0] === heroNode && !svg.querySelector('polyline'), 'expiry removes trail nodes and retains the hero node');
    render({ ...tail, visible: false });
    check(svg.style.opacity === '0', 'hero trails follow scene visibility');
    render(expired);
    check(!svg.querySelector('polyline') && svg.style.opacity === '1', 'fresh snapshot after reconnect contains no expired trail');
}
const erasurePath = process.argv[3];
if (erasurePath) {
    const { before, after } = JSON.parse(fs.readFileSync(erasurePath, 'utf8'));
    render(before);
    const node = svg.children[0];
    render(after);
    const line = svg.children[0].querySelector('polyline');
    check(svg.children[0] === node, 'erasure reuses the existing OBS trail node');
    check(+line.getAttribute('stroke-dashoffset') === after.items[0].trailDashOffset && after.items[0].trailDashOffset > 0, 'erased distance offsets the dash pattern so surviving dashes stay anchored');
    check(line.getAttribute('points') === after.items[0].points.map(p => `${p.x},${p.y}`).join(' '), 'OBS erasure follows desktop interpolated cutoff coordinates');
    render(before);
    check(+svg.children[0].querySelector('polyline').getAttribute('stroke-dashoffset') === 0, 'new or restored paths reset their dash offset');
}
const textFixturePath = process.argv[4];
if (textFixturePath && !textFixturePath.startsWith('--')) {
    const state = JSON.parse(fs.readFileSync(textFixturePath, 'utf8'));
    render(state);
    const item = state.items[0], node = svg.children[0], text = node.querySelector('text');
    const spans = [...text.querySelectorAll('tspan')];
    check(spans.length === item.label.lines.length && spans.length > 2, 'desktop wrapped text emits one SVG tspan per line');
    check(spans.map(s => s.textContent).join('') === item.text, 'OBS preserves complete Chinese English and emoji content');
    check(spans.every((s, n) => +s.getAttribute('x') === item.x + 10 && +s.getAttribute('y') === item.y + item.label.baseline + n * item.label.lineHeight), 'OBS multiline positions use desktop baselines and line height');
    check(+node.querySelector('rect').getAttribute('height') === item.label.height && text.getAttribute('text-anchor') === 'start', 'OBS multiline background and alignment match desktop metrics');
    render({ ...state, items: [{ ...item, text: '<B>\n第二行', label: { ...item.label, lines: ['<B>', '第二行'] } }] });
    check(svg.children[0] === node && text.querySelectorAll('tspan').length === 2 && !svg.querySelector('B'), 'editing multiline text reuses its node removes old lines and escapes markup');
    render({ ...state, items: [{ ...item, text: '单行', label: { width: 120, height: 50, baseline: 35 } }] });
    check(text.textContent === '单行' && text.querySelectorAll('tspan').length === 0, 'legacy single-line scene replaces multiline spans cleanly');
}
console.log(JSON.stringify({ passed, domOnly: true }));
