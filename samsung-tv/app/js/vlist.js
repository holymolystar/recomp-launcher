// A list that's fast however long it is (a provider can have 50,000 channels): only the rows on the screen exist,
// and they're reused as the picked row moves.
function VList(el, rowH, draw) {
  this.el = el; this.rowH = rowH; this.draw = draw;
  this.items = []; this.sel = 0; this.top = 0; this.rows = [];
  this.bar = document.createElement('div'); this.bar.className = 'scrollbar'; el.appendChild(this.bar);
}
VList.prototype.visible = function () { return Math.max(1, Math.floor(this.el.clientHeight / this.rowH)); };
VList.prototype.setItems = function (items, sel) {
  this.items = items || []; this.sel = Math.max(0, Math.min(sel || 0, this.items.length - 1)); this.top = -1;
  this.render(true);
};
VList.prototype.move = function (d) {
  if (!this.items.length) return false;
  var n = Math.max(0, Math.min(this.items.length - 1, this.sel + d));
  if (n === this.sel) return false;
  this.sel = n; this.render(); return true;
};
VList.prototype.page = function (d) { return this.move(d * (this.visible() - 1)); };
VList.prototype.current = function () { return this.items[this.sel]; };
VList.prototype.render = function (force) {
  var vis = this.visible(), n = this.items.length;
  // the picked row stays in view, two rows from the edge where it can
  var top = this.top < 0 ? this.sel - Math.floor(vis / 3) : this.top;
  if (this.sel < top + 2) top = this.sel - 2;
  if (this.sel > top + vis - 3) top = this.sel - vis + 3;
  top = Math.max(0, Math.min(top, n - vis));
  var moved = top !== this.top; this.top = top;
  while (this.rows.length < vis + 0) { var r = document.createElement('div'); r.className = 'vrow'; r.style.height = (this.rowH - 8) + 'px'; this.el.appendChild(r); this.rows.push(r); }
  for (var i = 0; i < this.rows.length; i++) {
    var row = this.rows[i], idx = top + i, it = this.items[idx];
    if (!it) { row.style.display = 'none'; row._it = null; continue; }
    row.style.display = ''; row.style.top = (i * this.rowH) + 'px';
    var selNow = idx === this.sel;
    if (force || moved || row._it !== it || row._sel !== selNow) { this.draw(row, it, selNow, idx); row._it = it; }
    row._sel = selNow; row.className = 'vrow' + (selNow ? ' sel' : '');
  }
  if (n > vis) {
    var h = this.el.clientHeight, bh = Math.max(40, h * vis / n);
    this.bar.style.display = ''; this.bar.style.height = bh + 'px'; this.bar.style.top = ((h - bh) * top / (n - vis)) + 'px';
  } else this.bar.style.display = 'none';
};
VList.prototype.redraw = function () { this.render(true); };
