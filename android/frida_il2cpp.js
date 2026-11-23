/*
 * frida_il2cpp.js —— Unity IL2CPP 通用修改器
 * 适用: libil2cpp.so + global-metadata.dat（probe.py 判定 runtime=IL2CPP）
 *
 * 用法（pc 端）:
 *   frida -U -f com.xxx.worstclassever -l frida_il2cpp.js --no-pause
 *   或已启动: frida -U -n WorstClassEver -l frida_il2cpp.js
 *
 * 控制台命令:
 *   cls <关键字>                列出类名/命名空间含关键字的类
 *   fields <类名>               列出该类的全部字段 + 偏移 + 类型
 *   methods <类名>              列出方法 + 参数个数 + RVA
 *   inst <类名>                 找该类的一个实例（hook 构造函数抓 this 指针）
 *   get <类名> <字段名>         读静态字段 / 最近实例的实例字段
 *   set <类名> <字段名> <值>    写静态字段 / 最近实例的实例字段
 *   god <类名> <字段名> <值>    锁值: 该类任意方法被调用时把字段改回 <值>
 *   hook <类名> <方法名>        打印每次调用(含 this 与参数)
 *   unhook                      清掉所有 hook
 */
'use strict';

const LIB = 'libil2cpp.so';
const lib = Process.findModuleByName(LIB) || Process.findModuleByName('libil2cpp.so');
if (!lib) { console.log('[-] 找不到 ' + LIB + ' —— 这游戏不是 IL2CPP，去用 frida_mono.js'); }

function E(n) { return Module.findExportByName(LIB, n); }
function call(name, ret, args) {
  const p = E(name);
  if (!p) { console.log('[-] miss export ' + name); return null; }
  const f = new NativeFunction(p, ret, args);
  return f;
}

const api = {
  domain_get: call('il2cpp_domain_get', 'pointer', []),
  domain_get_assemblies: call('il2cpp_domain_get_assemblies', 'pointer', ['pointer', 'pointer']),
  assembly_get_image: call('il2cpp_assembly_get_image', 'pointer', ['pointer']),
  image_get_name: call('il2cpp_image_get_name', 'pointer', ['pointer']),
  image_get_class_count: call('il2cpp_image_get_class_count', 'uint', ['pointer']),
  image_get_class: call('il2cpp_image_get_class', 'pointer', ['pointer', 'uint']),
  class_get_name: call('il2cpp_class_get_name', 'pointer', ['pointer']),
  class_get_namespace: call('il2cpp_class_get_namespace', 'pointer', ['pointer']),
  class_get_fields: call('il2cpp_class_get_fields', 'pointer', ['pointer', 'pointer']),
  class_get_methods: call('il2cpp_class_get_methods', 'pointer', ['pointer', 'pointer']),
  class_get_parent: call('il2cpp_class_get_parent', 'pointer', ['pointer']),
  class_get_type: call('il2cpp_class_get_type', 'pointer', ['pointer']),
  type_get_object: call('il2cpp_type_get_object', 'pointer', ['pointer']),
  type_get_name: call('il2cpp_type_get_name', 'pointer', ['pointer']),
  field_get_name: call('il2cpp_field_get_name', 'pointer', ['pointer']),
  field_get_offset: call('il2cpp_field_get_offset', 'int', ['pointer']),
  field_get_type: call('il2cpp_field_get_type', 'pointer', ['pointer']),
  field_get_flags: call('il2cpp_field_get_flags', 'uint', ['pointer']),
  field_static_get_value: call('il2cpp_field_static_get_value', 'void', ['pointer', 'pointer']),
  field_static_set_value: call('il2cpp_field_static_set_value', 'void', ['pointer', 'pointer']),
  method_get_name: call('il2cpp_method_get_name', 'pointer', ['pointer']),
  method_get_param_count: call('il2cpp_method_get_param_count', 'uint', ['pointer']),
  method_get_rva: call('il2cpp_method_get_rva', 'uint', ['pointer']),
  method_get_flags: call('il2cpp_method_get_flags', 'uint', ['pointer', 'pointer']),
};
function isStaticMethod(m) {
  try {
    const iflags = Memory.alloc(4);
    return (api.method_get_flags(m, iflags) & 0x10) !== 0;   // METHOD_ATTRIBUTE_STATIC
  } catch (e) { return false; }
}
if (E('il2cpp_method_get_pointer')) api.method_get_pointer = call('il2cpp_method_get_pointer', 'pointer', ['pointer']);
if (E('il2cpp_compile_method')) api.compile_method = call('il2cpp_compile_method', 'pointer', ['pointer', 'pointer']);

const rstr = p => { try { return p.isNull() ? '' : p.readUtf8String(); } catch (e) { return ''; } };

/* ---------- 遍历所有类 ---------- */
let CLASSES = null;
function allClasses() {
  if (CLASSES) return CLASSES;
  CLASSES = [];
  const n = Memory.alloc(4);
  const asms = api.domain_get_assemblies(api.domain_get(), n);
  const count = n.readUInt();
  for (let i = 0; i < count; i++) {
    const img = api.assembly_get_image(asms.add(i * Process.pointerSize).readPointer());
    if (img.isNull()) continue;
    const cc = api.image_get_class_count(img);
    const iname = rstr(api.image_get_name(img));
    for (let j = 0; j < cc; j++) {
      const k = api.image_get_class(img, j);
      if (k.isNull()) continue;
      CLASSES.push({ k, name: rstr(api.class_get_name(k)), ns: rstr(api.class_get_namespace(k)), img: iname });
    }
  }
  console.log('[+] 已索引 ' + CLASSES.length + ' 个类');
  return CLASSES;
}

function findClasses(kw) {
  kw = kw.toLowerCase();
  return allClasses().filter(c => (c.ns + '.' + c.name).toLowerCase().includes(kw));
}

function dumpFields(k) {
  const it = Memory.alloc(Process.pointerSize);
  it.writePointer(ptr(0));
  const out = [];
  for (;;) {
    const f = api.class_get_fields(k, it);
    if (f.isNull()) break;
    const flags = api.field_get_flags(f);
    const isStatic = (flags & 0x10) !== 0;
    out.push({
      name: rstr(api.field_get_name(f)), offset: api.field_get_offset(f),
      type: rstr(api.type_get_name(api.field_get_type(f))), isStatic
    });
  }
  return out;
}

/* ---------- 实例追踪 ---------- */
const instances = new Map();   // className -> Set<ptr>

function traceCtor(className) {
  const cands = findClasses(className).slice(0, 1);
  if (!cands.length) { console.log('[-] 没这个类: ' + className); return; }
  const k = cands[0].k;
  const it = Memory.alloc(Process.pointerSize);
  it.writePointer(ptr(0));
  let hooked = 0;
  for (;;) {
    const m = api.class_get_methods(k, it);
    if (m.isNull()) break;
    const nm = rstr(api.method_get_name(m));
    let fp = null;
    try { if (api.method_get_pointer) fp = api.method_get_pointer(m); } catch (e) {}
    if (!fp || fp.isNull()) {
      try {
        const rva = api.method_get_rva(m);
        if (rva) fp = lib.base.add(rva);
      } catch (e) {}
    }
    if (!fp || fp.isNull()) continue;
    if (!/^(\.ctor|\.cctor|Awake|Start|OnEnable|Init)$/.test(nm)) continue;
    try {
      Interceptor.attach(fp, {
        onEnter(args) {
          if (!(instances.get(className) instanceof Set)) instances.set(className, new Set());
          const set = instances.get(className);
          if (set.size < 64) set.add(args[0]);
        }
      });
      hooked++;
    } catch (e) {}
  }
  console.log(`[+] ${className} 已挂 ${hooked} 个构造/初始化函数；进一次游戏场景后 instances 就有值了`);
}

function anyInstance(className) {
  const s = instances.get(className);
  if (s && s.size) return [...s][0];
  return null;
}

/* ---------- get / set ---------- */
function fieldOf(className, fieldName) {
  const cs = findClasses(className);
  for (const c of cs) {
    const fs = dumpFields(c.k);
    const f = fs.find(x => x.name.toLowerCase() === fieldName.toLowerCase());
    if (f) return { cls: c, field: f, k: c.k };
  }
  return null;
}

function getField(className, fieldName) {
  const r = fieldOf(className, fieldName);
  if (!r) { console.log('[-] 找不到字段 ' + className + '.' + fieldName); return; }
  if (r.field.isStatic) {
    // 静态字段需要 FieldInfo*，用 class_get_fields 重新取一次指针
    const k = r.k, it = Memory.alloc(Process.pointerSize);
    it.writePointer(ptr(0));
    for (;;) {
      const f = api.class_get_fields(k, it);
      if (f.isNull()) break;
      if (rstr(api.field_get_name(f)) === r.field.name) {
        const buf = Memory.alloc(16);
        api.field_static_get_value(f, buf);
        console.log(`[get] ${className}.${fieldName} = ${buf.readS32()} / f=${buf.readFloat()}`);
        return;
      }
    }
  } else {
    const inst = anyInstance(r.cls.name);
    if (!inst) { console.log('[-] 还没有 ' + className + ' 的实例，先 inst ' + className + ' 并进一次场景'); return; }
    const a = inst.add(r.field.offset);
    console.log(`[get] inst(${inst}).${fieldName} = i32=${a.readS32()} f32=${a.readFloat()}`);
  }
}

function writeField(className, fieldName, val) {
  const r = fieldOf(className, fieldName);
  if (!r) { console.log('[-] 找不到字段 ' + className + '.' + fieldName); return; }
  const v = Number(val);
  if (!r.field.isStatic) {
    const inst = anyInstance(r.cls.name);
    if (!inst) { console.log('[-] 无实例'); return; }
    const a = inst.add(r.field.offset);
    if (/single|float/i.test(r.field.type)) a.writeFloat(v); else a.writeS32(v | 0);
    console.log(`[set] inst+${r.field.offset} = ${v}`);
  } else {
    const k = r.k, it = Memory.alloc(Process.pointerSize);
    it.writePointer(ptr(0));
    for (;;) {
      const f = api.class_get_fields(k, it);
      if (f.isNull()) break;
      if (rstr(api.field_get_name(f)) === r.field.name) {
        const buf = Memory.alloc(16);
        if (/single|float/i.test(r.field.type)) buf.writeFloat(v); else buf.writeS32(v | 0);
        api.field_static_set_value(f, buf);
        console.log(`[set] static ${className}.${fieldName} = ${v}`);
        return;
      }
    }
  }
}

/* ---------- 锁值 ---------- */
let godHooks = [];
function god(className, fieldName, val) {
  const r = fieldOf(className, fieldName);
  if (!r) { console.log('[-] 找不到字段'); return; }
  const k = r.k, it = Memory.alloc(Process.pointerSize);
  it.writePointer(ptr(0));
  let n = 0;
  for (;;) {
    const m = api.class_get_methods(k, it);
    if (m.isNull()) break;
    if (isStaticMethod(m)) continue;              // 静态方法没有 this，会写坏别的内存
    let fp = null;
    try { if (api.method_get_pointer) fp = api.method_get_pointer(m); } catch (e) {}
    if (!fp || fp.isNull()) { const rva = api.method_get_rva(m); if (rva) fp = lib.base.add(rva); }
    if (!fp || fp.isNull()) continue;
    const v = Number(val), off = r.field.offset, isF = /single|float/i.test(r.field.type);
    try {
      const l = Interceptor.attach(fp, {
        onEnter(args) {
          const self = args[0];
          if (self.isNull()) return;
          const a = self.add(off);
          if (isF) a.writeFloat(v); else a.writeS32(v | 0);
        }
      });
      godHooks.push(l); n++;
    } catch (e) {}
  }
  console.log(`[+] god: ${className}.${fieldName} 锁定 ${val}（挂了 ${n} 个方法）`);
}

function hookMethod(className, methodName) {
  const cs = findClasses(className);
  if (!cs.length) { console.log('[-] 没这个类'); return; }
  const k = cs[0].k, it = Memory.alloc(Process.pointerSize);
  it.writePointer(ptr(0));
  let n = 0;
  for (;;) {
    const m = api.class_get_methods(k, it);
    if (m.isNull()) break;
    const nm = rstr(api.method_get_name(m));
    if (methodName && nm !== methodName) continue;
    let fp = null;
    try { if (api.method_get_pointer) fp = api.method_get_pointer(m); } catch (e) {}
    if (!fp || fp.isNull()) { const rva = api.method_get_rva(m); if (rva) fp = lib.base.add(rva); }
    if (!fp || fp.isNull()) continue;
    const pc = api.method_get_param_count(m);
    try {
      Interceptor.attach(fp, {
        onEnter(args) {
          const parts = [];
          for (let i = 1; i <= Math.min(pc, 6); i++) parts.push(args[i].toString());
          console.log(`[call] ${className}.${nm} this=${args[0]} args=${parts.join(',')}`);
        }
      });
      n++;
    } catch (e) {}
  }
  console.log(`[+] hook ${className}.${methodName || '*'} -> ${n} 个`);
}

/* ---------- REPL ---------- */
const handlers = {
  cls: a => { const r = findClasses(a[0] || ''); r.slice(0, 60).forEach(c => console.log(`${c.ns}.${c.name}`)); console.log(`共 ${r.length}`); },
  fields: a => { const cs = findClasses(a[0]); cs.slice(0, 3).forEach(c => { console.log(`--- ${c.ns}.${c.name}`); dumpFields(c.k).forEach(f => console.log(`   ${f.isStatic ? 'static' : 'inst  '} ${f.name}  off=${f.offset}  ${f.type}`)); }); },
  methods: a => { const cs = findClasses(a[0]); cs.slice(0, 3).forEach(c => { console.log(`--- ${c.ns}.${c.name}`); const it = Memory.alloc(Process.pointerSize); it.writePointer(ptr(0)); for (;;) { const m = api.class_get_methods(c.k, it); if (m.isNull()) break; console.log(`   ${rstr(api.method_get_name(m))} (${api.method_get_param_count(m)}) rva=${api.method_get_rva(m).toString(16)}`); } }); },
  inst: a => traceCtor(a[0]),
  get: a => getField(a[0], a[1]),
  set: a => writeField(a[0], a[1], a[2]),
  god: a => god(a[0], a[1], a[2]),
  hook: a => hookMethod(a[0], a[1]),
  unhook: () => { godHooks.forEach(l => { try { l.detach(); } catch (e) {} }); godHooks = []; console.log('[+] 已清 hook'); },
};

globalThis.DSH = handlers;
setTimeout(() => {
  if (!E('il2cpp_domain_get')) return;
  console.log('[*] 就绪。REPL 里用 DSH.xxx(...) 调用: cls / fields / methods / inst / get / set / god / hook / unhook');
  console.log('    例: DSH.cls("GameManager")   DSH.fields("GameManager")   DSH.god("GameManager","money",999999)');
}, 500);
