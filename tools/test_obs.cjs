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
console.log(JSON.stringify({ passed, domOnly: true }));
