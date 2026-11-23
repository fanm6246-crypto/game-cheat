/*
 * frida_mono.js —— Unity Mono 通用修改器
 * 适用: libmonobdwgc-2.0.so / libmono.so + assets/bin/Data/Managed/*.dll
 *      （probe.py 判定 runtime=Mono 时用；这是最省事的一类，Mono 没混淆的话类名全可见）
 *
 * 用法: frida -U -f <包名> -l frida_mono.js --no-pause
 *
 * 命令:
 *   asm                         列出已加载程序集
 *   cls <关键字>                找类
 *   fields <类名>               字段+偏移+类型
 *   methods <类名>              方法列表
 *   get <类名> <字段名>         读（静态或最近实例）
 *   set <类名> <字段名> <值>    写
 *   god <类名> <字段名> <值>    锁值
 *   hook <类名> <方法名>        打印调用
 */
'use strict';

let LIB = null;
for (const n of ['libmonobdwgc-2.0.so', 'libmonobdwgc.so', 'libmono.so']) {
  if (Module.findModuleByName(n)) { LIB = n; break; }
}
if (!LIB) { console.log('[-] 找不到 mono 运行时，这游戏不是 Mono，去用 frida_il2cpp.js'); }

function E(n) { return Module.findExportByName(LIB, n); }
function F(n, ret, args) { const p = E(n); return p ? new NativeFunction(p, ret, args) : null; }

const api = {
  get_root_domain: F('mono_get_root_domain', 'pointer', []),
  assembly_foreach: F('mono_assembly_foreach', 'void', ['pointer', 'pointer']),
  assembly_get_image: F('mono_assembly_get_image', 'pointer', ['pointer']),
  image_get_name: F('mono_image_get_name', 'pointer', ['pointer']),
  class_from_name: F('mono_class_from_name', 'pointer', ['pointer', 'pointer', 'pointer']),
  class_get_name: F('mono_class_get_name', 'pointer', ['pointer']),
  class_get_namespace: F('mono_class_get_namespace', 'pointer', ['pointer']),
  class_get_fields: F('mono_class_get_fields', 'pointer', ['pointer', 'pointer']),
  class_get_methods: F('mono_class_get_methods', 'pointer', ['pointer', 'pointer']),
  field_get_name: F('mono_field_get_name', 'pointer', ['pointer']),
  field_get_offset: F('mono_field_get_offset', 'int', ['pointer']),
  field_get_type: F('mono_field_get_type', 'pointer', ['pointer']),
  field_get_flags: F('mono_field_get_flags', 'uint', ['pointer']),
  field_get_value: F('mono_field_get_value', 'void', ['pointer', 'pointer', 'pointer']),
  field_set_value: F('mono_field_set_value', 'void', ['pointer', 'pointer', 'pointer']),
  method_get_name: F('mono_method_get_name', 'pointer', ['pointer']),
  method_get_param_count: F('mono_method_get_param_count', 'uint', ['pointer']),
  compile_method: F('mono_compile_method', 'pointer', ['pointer']) ||
                  F('mono_jit_compile_method', 'pointer', ['pointer']),
  type_get_name: F('mono_type_get_name', 'pointer', ['pointer']),
};
const rstr = p => { try { return p.isNull() ? '' : p.readUtf8String(); } catch (e) { return ''; } };

/* ---------- 程序集索引 ---------- */
const IMAGES = [];
let scanned = false;
function scanAssemblies() {
  if (scanned) return IMAGES;
  scanned = true;
  const cb = new NativeCallback(function (asm, ud) {
    try {
      const img = api.assembly_get_image(asm);
      if (!img.isNull()) IMAGES.push({ img, name: rstr(api.image_get_name(img)) });
    } catch (e) {}
  }, 'void', ['pointer', 'pointer']);
  api.assembly_foreach(cb, ptr(0));
  console.log('[+] 程序集: ' + IMAGES.map(i => i.name).join(', '));
  return IMAGES;
}

function imgOf(name) {
  const all = scanAssemblies();
  return (all.find(i => i.name.toLowerCase() === name.toLowerCase()) ||
          all.find(i => i.name.toLowerCase().includes(name.toLowerCase()))) || null;
}

function cls(name) {
  const i = name.lastIndexOf('.');
  const ns = i > 0 ? name.slice(0, i) : '';
  const sn = i > 0 ? name.slice(i + 1) : name;
  for (const im of scanAssemblies()) {
    const k = api.class_from_name(im.img, Memory.allocUtf8String(ns), Memory.allocUtf8String(sn));
    if (!k.isNull()) return { k, name: sn, ns, img: im.name };
  }
  return null;
}

function clsFuzzy(kw) {
  kw = kw.toLowerCase();
  const found = [];
  for (const im of scanAssemblies()) {
    // 无"枚举所有类"的稳定导出，用常见命名空间逐类试
    const tries = new Set();
    const n = rstr(api.image_get_name(im.img));
    const namespaces = ['', 'Assembly-CSharp', 'Game', 'Game.Gameplay', 'Scripts', 'UnityEngine', 'App', 'Core'];
    for (const ns of namespaces) for (const c of [kw, kw[0].toUpperCase() + kw.slice(1)]) {
      const key = ns + '|' + c;
      if (tries.has(key)) continue;
      tries.add(key);
      try {
        const k = api.class_from_name(im.img, Memory.allocUtf8String(ns), Memory.allocUtf8String(c));
        if (!k.isNull()) { found.push({ k, name: rstr(api.class_get_name(k)), ns: rstr(api.class_get_namespace(k)), img: n }); }
      } catch (e) {}
    }
  }
  return found;
}

function dumpFields(k, name) {
  const it = Memory.alloc(Process.pointerSize); it.writePointer(ptr(0));
  const out = [];
  for (;;) {
    const f = api.class_get_fields(k, it);
    if (f.isNull()) break;
    const fl = api.field_get_flags(f);
    out.push({ name: rstr(api.field_get_name(f)), offset: api.field_get_offset(f),
               type: rstr(api.type_get_name(api.field_get_type(f))), isStatic: (fl & 0x10) !== 0 });
  }
  return out;
}

/* ---------- 实例追踪 ---------- */
const instances = new Map();
function traceCtor(className) {
  const c = cls(className);
  if (!c) { console.log('[-] 没这个类'); return; }
  const it = Memory.alloc(Process.pointerSize); it.writePointer(ptr(0));
  let n = 0;
  for (;;) {
    const m = api.class_get_methods(c.k, it);
    if (m.isNull()) break;
    const nm = rstr(api.method_get_name(m));
    if (!/^\.(ctor|static ctor)$/.test(nm)) continue;
    let fp = null;
    try { fp = api.compile_method(m); } catch (e) {}
    if (!fp || fp.isNull()) continue;
    try {
      Interceptor.attach(fp, { onEnter(a) {
        if (!(instances.get(className) instanceof Set)) instances.set(className, new Set());
        const s = instances.get(className); if (s.size < 64) s.add(a[0]);
      } });
      n++;
    } catch (e) {}
  }
  console.log(`[+] ${className}: 挂到 ${n} 个构造函数`);
}
function anyInstance(n) { const s = instances.get(n); return s && s.size ? [...s][0] : null; }

/* ---------- 读写 ---------- */
function fieldPtr(className, fieldName) {
  const c = cls(className);
  if (!c) return null;
  const it = Memory.alloc(Process.pointerSize); it.writePointer(ptr(0));
  for (;;) {
    const f = api.class_get_fields(c.k, it);
    if (f.isNull()) break;
    if (rstr(api.field_get_name(f)).toLowerCase() === fieldName.toLowerCase()) {
      return { f, meta: dumpFields(c.k).find(x => x.name.toLowerCase() === fieldName.toLowerCase()), cls: c };
    }
  }
  return null;
}

function getField(className, fieldName) {
  const r = fieldPtr(className, fieldName);
  if (!r) { console.log('[-] 找不到字段'); return; }
  const buf = Memory.alloc(16);
  if (r.meta.isStatic) { api.field_get_value(r.f, ptr(0), buf); }
  else {
    const inst = anyInstance(className);
    if (!inst) { console.log('[-] 无实例，先 inst'); return; }
    api.field_get_value(r.f, inst, buf);
  }
  console.log(`[get] ${className}.${fieldName} = i32=${buf.readS32()} f32=${buf.readFloat()}`);
}

function setField(className, fieldName, val) {
  const r = fieldPtr(className, fieldName);
  if (!r) { console.log('[-] 找不到字段'); return; }
  const v = Number(val), isF = /single|float/i.test(r.meta.type);
  const buf = Memory.alloc(16);
  if (isF) buf.writeFloat(v); else buf.writeS32(v | 0);
  if (r.meta.isStatic) api.field_set_value(r.f, ptr(0), buf);
  else {
    const inst = anyInstance(className);
    if (!inst) { console.log('[-] 无实例'); return; }
    api.field_set_value(r.f, inst, buf);
  }
  console.log(`[set] ${className}.${fieldName} = ${v}`);
}

/* ---------- god 锁值 / hook ---------- */
const hooks = [];
function god(className, fieldName, val) {
  const r = fieldPtr(className, fieldName);
  if (!r) return;
  const c = cls(className);
  const it = Memory.alloc(Process.pointerSize); it.writePointer(ptr(0));
  const v = Number(val), off = r.meta.offset, isF = /single|float/i.test(r.meta.type);
  let n = 0;
  for (;;) {
    const m = api.class_get_methods(c.k, it);
    if (m.isNull()) break;
    let fp = null; try { fp = api.compile_method(m); } catch (e) {}
    if (!fp || fp.isNull()) continue;
    try {
      hooks.push(Interceptor.attach(fp, { onEnter(a) {
        if (a[0].isNull()) return;
        const p = a[0].add(off);
        if (isF) p.writeFloat(v); else p.writeS32(v | 0);
      } }));
      n++;
    } catch (e) {}
  }
  console.log(`[+] god ${className}.${fieldName}=${val}（${n} 个方法）`);
}

function hookMethod(className, methodName) {
  const c = cls(className);
  if (!c) { console.log('[-] 没这个类'); return; }
  const it = Memory.alloc(Process.pointerSize); it.writePointer(ptr(0));
  let n = 0;
  for (;;) {
    const m = api.class_get_methods(c.k, it);
    if (m.isNull()) break;
    const nm = rstr(api.method_get_name(m));
    if (methodName && nm !== methodName) continue;
    let fp = null; try { fp = api.compile_method(m); } catch (e) {}
    if (!fp || fp.isNull()) continue;
    const pc = api.method_get_param_count(m);
    try {
      hooks.push(Interceptor.attach(fp, { onEnter(a) {
        const ps = []; for (let i = 1; i <= Math.min(pc, 6); i++) ps.push(a[i].toString());
        console.log(`[call] ${className}.${nm} this=${a[0]} (${ps.join(',')})`);
      } }));
      n++;
    } catch (e) {}
  }
  console.log(`[+] hook ${className}.${methodName || '*'} -> ${n}`);
}

const handlers = {
  asm: () => scanAssemblies().forEach(i => console.log(i.name)),
  cls: a => clsFuzzy(a[0] || '').forEach(c => console.log(`${c.ns}.${c.name}  [${c.img}]`)),
  fields: a => { const c = cls(a[0]); if (!c) { console.log('[-] 没这个类'); return; }
    dumpFields(c.k).forEach(f => console.log(`  ${f.isStatic ? 'static' : 'inst  '} ${f.name}  off=${f.offset}  ${f.type}`)); },
  methods: a => { const c = cls(a[0]); if (!c) return; const it = Memory.alloc(Process.pointerSize); it.writePointer(ptr(0));
    for (;;) { const m = api.class_get_methods(c.k, it); if (m.isNull()) break;
      console.log(`  ${rstr(api.method_get_name(m))} (${api.method_get_param_count(m)})`); } },
  inst: a => traceCtor(a[0]),
  get: a => getField(a[0], a[1]),
  set: a => setField(a[0], a[1], a[2]),
  god: a => god(a[0], a[1], a[2]),
  hook: a => hookMethod(a[0], a[1]),
};
globalThis.DSH = handlers;
setTimeout(() => {
  if (!LIB) return;
  scanAssemblies();
  console.log('[*] 就绪。命令: asm / cls / fields / methods / inst / get / set / god / hook');
}, 800);
