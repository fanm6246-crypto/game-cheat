// WCE Hack v2 - worst class ever trainer (official mod API / Harmony)
// build: csc.exe (C#5) against game Managed dlls
// v2: chinese UI + auto aim / homing for sword slash (teacher melee), eraser and chalk
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WorstClassEver.ModAPI;

namespace WceHack
{
    public class HackPlugin : IMod
    {
        public static HackPlugin Inst;
        public static ModContext Ctx;

        public void OnLoad(ModContext context)
        {
            Inst = this;
            Ctx = context;
            Log("OnLoad begin");
            try { ThirdPerson.Hook(); Log("third person hook ok"); } catch (Exception e) { Log("tp hook fail " + e.Message); }
            try
            {
                var go = new GameObject("WCE_HACK");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<HackBehaviour>();
                Log("behaviour attached");
            }
            catch (Exception e)
            {
                Log("attach fail: " + e);
            }
            try
            {
                HackPatches.Install();
                HackPatches.Patch(typeof(CatchComponent), "CatchUnit", new Type[] { typeof(Unit) }, "Pre_CatchUnit", null);
        HackPatches.Patch(typeof(BeCatchedComponent), "OnGetCatched", new Type[] { typeof(Unit), typeof(EUnitPartType), typeof(bool) }, "Pre_OnGetCatched", null);
            HackPatches.Patch(typeof(StudentItemComponent), "ReduceItemDurability", null, "Pre_ReduceDur", null);
            HackPatches.Patch(typeof(StudentItemComponent), "ApplyThrown", null, null, "Post_ApplyThrown");
            HackPatches.Patch(typeof(StudentItemComponent), "OnThrown", new Type[] { typeof(Unit) }, "Pre_OnThrown", null);
            HackPatches.Patch(typeof(StudentItemComponent), "NotifyUseCompleted", new Type[] { typeof(Unit) }, "Pre_NotifyCompleted", null);
        HackPatches.Patch(typeof(StudentStunComponent), "DealStunEffect", new Type[] { typeof(EStunType), typeof(float) }, "Pre_DealStunEffect", null);
        HackPatches.Patch(typeof(StudentStunComponent), "ApplyStun", new Type[] { typeof(float), typeof(float) }, "Pre_ApplyStun", null);
                HackPatches.Patch(typeof(StudentStunComponent), "TryGetProjectileStun", new Type[] { typeof(float) }, "Pre_Stun", null);
                HackPatches.Patch(typeof(MouthItemUseComponent), "GetUseDuration", new Type[] { typeof(EStudentItemUseType) }, "Pre_UseDuration", null);
            HackPatches.Patch(typeof(ItemDispenserComponent), "get_HoldDuration", Type.EmptyTypes, "Pre_HoldDuration", null);
            HackPatches.Patch(typeof(LocomotionComponent), "OnUpdate", new Type[] { typeof(float) }, "Pre_Locomotion", null);
            HackPatches.Patch(typeof(StudentItemWeights), "OnRefillItemConsumed", new Type[] { typeof(ulong), typeof(int) }, "Pre_RefillConsumed", null);
            HackPatches.Patch(typeof(CameraComponent), "PlayStunHitCamera", null, "Pre_StunCam", null);
                HackPatches.Patch(typeof(CameraComponent), "ChangeCameraPos", new Type[] { typeof(bool) }, "Pre_NoCatchCam", null);
                HackPatches.Patch(typeof(CameraComponent), "RefreshCameraPos", new Type[] { typeof(bool) }, "Pre_NoCatchCam", null);
                HackPatches.Patch(typeof(CameraComponent), "ChangeFollow", new Type[] { typeof(Transform), typeof(bool) }, "Pre_NoCatchCam", null);
                HackPatches.Patch(typeof(CameraComponent), "ApplyPostureTransition", new Type[] { typeof(Transform), typeof(float) }, "Pre_NoCatchCam", null);
                HackPatches.Patch(typeof(CameraComponent), "AlignReleasedView", new Type[] { typeof(float), typeof(bool) }, "Pre_NoCatchCam", null);
                HackPatches.Patch(typeof(CameraComponent), "ResetCameraRotation", new Type[] { typeof(bool) }, "Pre_NoCatchCam", null);
                HackPatches.PatchGetter(typeof(Unit), "IsCameraLimited", "Post_NotLimited");
                WallHack.Apply();
                HackPatches.RegisterTaser();
        HackPatches.Patch(typeof(DrawerComponent), "OnActive", null, null, "Post_Active");
        Log("harmony patches installed");
            }
            catch (Exception e)
            {
                Log("patch fail: " + e);
            }
        }

        public static void Log(string s)
        {
            Debug.Log("[WceHack] " + s);
        }
    }

    // ---------- target selection shared by all patches ----------
    public static class Aim
    {
        public static bool AutoAim = false;  // 发射即锁定（v1.4 默认关，改用散射）
        public static bool Homing = false;   // 飞行中追踪拐弯（v1.4 默认关）
        public static float Range = 140f;
        public static bool NpcTarget = false;

        // ==== v3 追踪优化 ====
        public static bool Lead = true;        // 提前量：算目标速度，打提前点
        public static bool Smooth = true;      // 平滑转向：不瞬间掉头
        public static float TurnRate = 720f;   // 最大转向角速度(度/秒)
        public static float LockHold = 1.5f;   // 锁定保持(秒)，防止每帧换人乱抖
        public static float BodyOffset = 1.15f;

        static ulong _lockUid, _lockFor;
        static float _lockUntil;
        static readonly Dictionary<ulong, Vector3> _lastPos = new Dictionary<ulong, Vector3>();
        static readonly Dictionary<ulong, float> _lastT = new Dictionary<ulong, float>();
        static readonly Dictionary<ulong, Vector3> _vel = new Dictionary<ulong, Vector3>();

        public static UnitManager UM()
        {
            try { return UnitManager.Instance; }
            catch { return null; }
        }

        public static Unit ByUid(ulong uid)
        {
            if (uid == 0) return null;
            var um = UM();
            if (um == null || um.unitsDict == null) return null;
            Unit u;
            try { if (um.unitsDict.TryGetValue(uid, out u)) return u; }
            catch { }
            return null;
        }

        // 位置采样 → 目标速度（指数平滑，抗抖）
        public static Vector3 Velocity(Unit u)
        {
            ulong id;
            Vector3 p;
            try { id = u.UID; p = u.Position; }
            catch { return Vector3.zero; }
            float now = Time.realtimeSinceStartup;
            Vector3 v = Vector3.zero;
            Vector3 lp;
            float lt;
            if (_lastPos.TryGetValue(id, out lp) && _lastT.TryGetValue(id, out lt))
            {
                float dt = now - lt;
                if (dt > 0.02f && dt < 1.0f) v = (p - lp) / dt;
            }
            _lastPos[id] = p;
            _lastT[id] = now;
            Vector3 old;
            if (_vel.TryGetValue(id, out old)) v = Vector3.Lerp(old, v, 0.55f);
            _vel[id] = v;
            return v;
        }

        static bool Valid(Unit u, Vector3 from, ulong excludeUid)
        {
            if (u == null) return false;
            try
            {
                if (u.UID == excludeUid) return false;
                if (u.IsDead) return false;
                int sub = (int)u.UnitSubType;
                if (sub == 201) { }
                else if (sub == 202) { if (!NpcTarget) return false; }
                else return false;
                Vector3 p = u.Position;
                if (p.x == 0f && p.y == 0f && p.z == 0f) return false;
                if (Vector3.Distance(from, p) > Range) return false;
            }
            catch { return false; }
            return true;
        }

        // 找离 from 最近的活着的学生(201) / NPC学生(202)，排除 excludeUid
        public static Unit NearestStudent(Vector3 from, ulong excludeUid)
        {
            var um = UM();
            if (um == null || um.unitsDict == null) return null;
            Unit best = null;
            float bd = Range;
            try
            {
                foreach (var kv in um.unitsDict)
                {
                    var u = kv.Value;
                    if (!Valid(u, from, excludeUid)) continue;
                    float d = Vector3.Distance(from, u.Position);
                    if (d < bd) { bd = d; best = u; }
                }
            }
            catch { }
            return best;
        }

        // 锁定优先：同一目标保持 LockHold 秒，避免弹道每帧重新选人导致乱抖
        public static Unit Pick(Vector3 from, ulong excludeUid)
        {
            float now = Time.realtimeSinceStartup;
            if (_lockUntil > now && _lockFor == excludeUid)
            {
                var kept = ByUid(_lockUid);
                if (Valid(kept, from, excludeUid)) return kept;
            }
            var n = NearestStudent(from, excludeUid);
            if (n != null)
            {
                try { _lockUid = n.UID; }
                catch { _lockUid = 0; }
                _lockFor = excludeUid;
                _lockUntil = now + LockHold;
            }
            return n;
        }

        public static bool AimPoint(Vector3 from, ulong excludeUid, out Vector3 point, out Unit target)
        {
            return AimPoint(from, 0f, excludeUid, out point, out target);
        }

        // projSpeed > 0 时启用提前量预测（迭代两次收敛）
        public static bool AimPoint(Vector3 from, float projSpeed, ulong excludeUid, out Vector3 point, out Unit target)
        {
            point = Vector3.zero;
            target = Pick(from, excludeUid);
            if (target == null) return false;
            Vector3 p;
            try { p = target.Position + Vector3.up * BodyOffset; }
            catch { return false; }
            if (Lead && projSpeed > 0.5f)
            {
                Vector3 v = Velocity(target);
                if (v.sqrMagnitude > 0.04f)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        float t = Vector3.Distance(from, p) / projSpeed;
                        if (t > 2.5f) t = 2.5f;
                        p += v * t;
                    }
                }
            }
            point = p;
            return true;
        }

        public static Vector3 Dir(Vector3 from, out bool ok, ulong excludeUid)
        {
            return DirLead(from, 0f, out ok, excludeUid);
        }

        public static Vector3 DirLead(Vector3 from, float projSpeed, out bool ok, ulong excludeUid)
        {
            ok = false;
            Vector3 p;
            Unit t;
            if (!AimPoint(from, projSpeed, excludeUid, out p, out t)) return Vector3.forward;
            Vector3 d = p - from;
            if (d.sqrMagnitude < 0.04f) return Vector3.forward;
            ok = true;
            return d.normalized;
        }

        // 限角速度转向：一帧内不允许多转 TurnRate*dt 度
        public static Vector3 Turn(Vector3 cur, Vector3 want, float dt)
        {
            if (!Smooth) return want;
            if (cur.sqrMagnitude < 0.0001f) return want;
            float maxStep = TurnRate * Mathf.Max(dt, 0.005f);
            if (Vector3.Angle(cur, want) <= maxStep) return want;
            return Vector3.RotateTowards(cur, want, maxStep * Mathf.Deg2Rad, 0f);
        }
    }



    // ================= v4.3 防飞天：位置健全检查 =================
    // ================= v4.3 防飞天：位置健全检查 =================
    public static class Safe
    {
        public static bool On = true;
        public static float MaxY = 35f;    // 高于这个高度就认为飞了
        public static float MinY = -6f;    // 低于这个高度就认为掉出地图
        public static string Last = "";
        static float _next;

        public static void Tick()
        {
            if (!On) return;
            if (Fly.On) return;   // v4.9 飞行中不受"防飞天"限制
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 0.35f;

            var um = Aim.UM();
            if (um == null) return;
            Unit me = null;
            try { me = um.localPlayer; } catch { }
            if (me == null) return;

            Vector3 p;
            try { p = me.Position; } catch { return; }
            if (p.x == 0f && p.y == 0f && p.z == 0f) return;
            if (p.y <= MaxY && p.y >= MinY) return;

            Vector3 fix = p;
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, 300f, p.z), Vector3.down, out hit, 600f, ~0, QueryTriggerInteraction.Ignore))
                fix = hit.point + Vector3.up * 0.5f;
            else
                fix.y = 1f;

            try { me.SetPosition(fix); } catch { }
            try
            {
                var lc = me.GetComponent<LocomotionComponent>();
                if (lc != null) lc.SetPosition(fix);
            }
            catch { }
            Last = "已落地 y=" + fix.y.ToString("0.0");
        }
    }

    // ================= v5.3 御剑飞行：自己维护坐标，绝不被重力拉回去 =================
    public static class Fly
    {
        public static bool On = false;
        public static float Speed = 14f;      // 水平速度 m/s（中等偏快）
        public static float Vertical = 10f;   // 升降速度 m/s
        public static string Last = "";
        public static bool LocomotionFrozen = false;

        static CharacterController _cc;
        static Rigidbody[] _rbs;
        static bool _wasOn;
        static Vector3 _pos;          // 自己维护的坐标，不信任游戏写回来的值
        static bool _posInit;

        static Camera Cam()
        {
            Camera c = null;
            try { c = Camera.main; } catch { }
            if (c == null)
            {
                try { if (GameRoot.Inst != null) c = GameRoot.Inst.tempCamera; } catch { }
            }
            return c;
        }

        static void Enter(Unit me)
        {
            try { _pos = me.Position; _posInit = true; } catch { _posInit = false; }
            try { _cc = me.gameObject.GetComponent<CharacterController>(); if (_cc != null) _cc.enabled = false; } catch { }
            try
            {
                _rbs = me.gameObject.GetComponentsInChildren<Rigidbody>(true);
                if (_rbs != null)
                {
                    for (int i = 0; i < _rbs.Length; i++)
                    {
                        if (_rbs[i] == null) continue;
                        _rbs[i].useGravity = false;
                        _rbs[i].drag = 0f;
                        _rbs[i].velocity = Vector3.zero;
                    }
                }
            }
            catch { }
            try
            {
                var lc = me.GetComponent<LocomotionComponent>();
                if (lc != null) LocomotionFrozen = true;
            }
            catch { }
        }

        static void Exit(Unit me)
        {
            _posInit = false;
            try { if (_cc != null) _cc.enabled = true; } catch { }
            try
            {
                if (_rbs != null)
                    for (int i = 0; i < _rbs.Length; i++)
                        if (_rbs[i] != null) _rbs[i].useGravity = true;
                _rbs = null;
            }
            catch { }
            try { LocomotionFrozen = false; } catch { }
        }

        public static bool Pre_Locomotion()
        {
            return !LocomotionFrozen;   // 飞行中直接跳过游戏自己的移动/重力更新
        }

        static void Push(Unit me, Vector3 np)
        {
            try { me.transform.position = np; } catch { }
            try { me.SetPosition(np); } catch { }
            try
            {
                var lc = me.GetComponent<LocomotionComponent>();
                if (lc != null) lc.SetPosition(np);
            }
            catch { }
        }

        // LateUpdate 再压一次，游戏在同帧里怎么改都白改
        public static void LateTick()
        {
            if (!On || !_posInit) return;
            Unit me = null;
            try { var um = Aim.UM(); if (um != null) me = um.localPlayer; } catch { }
            if (me == null) return;
            Push(me, _pos);
        }

        public static void Tick()
        {
            var um = Aim.UM();
            if (um == null) { _wasOn = On; return; }
            Unit me = null;
            try { me = um.localPlayer; } catch { }
            if (me == null) { _wasOn = On; return; }

            if (On != _wasOn)
            {
                _wasOn = On;
                if (On) Enter(me); else Exit(me);
            }
            if (!On) return;

            Vector3 p;
            try { p = me.Position; } catch { return; }
            if (!_posInit)
            {
                if (p.x == 0f && p.y == 0f && p.z == 0f) return;
                _pos = p;
                _posInit = true;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f || dt > 0.2f) dt = 0.016f;

            Vector3 fwd = Vector3.forward;
            Camera cam = Cam();
            if (cam != null)
            {
                fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                fwd.Normalize();
            }
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += fwd;
            if (Input.GetKey(KeyCode.S)) move -= fwd;
            if (Input.GetKey(KeyCode.D)) move += right;
            if (Input.GetKey(KeyCode.A)) move -= right;
            if (move.sqrMagnitude > 1f) move.Normalize();

            // 垂直速度自己算，不按键就是 0 —— 绝不交给重力
            bool up = Input.GetKey(KeyCode.Space);
            bool down = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            float vy = 0f;
            if (up && !down) vy = Vertical;
            else if (down && !up) vy = -Vertical;

            _pos += move * (Speed * dt) + Vector3.up * (vy * dt);
            if (_pos.y < -5f) _pos.y = -5f;
            if (_pos.y > 400f) _pos.y = 400f;

            Push(me, _pos);
            Last = "y=" + _pos.y.ToString("0.0");
        }
    }

    // ================= v4.9 无限投掷物：补货冷却永远清空 =================
    public static class Stock
    {
        public static bool On = true;
        public static string Last = "";
        static FieldInfo _fCd;
        static MethodInfo _mClear;
        static bool _cached;
        static float _next;

        static void Cache()
        {
            if (_cached) return;
            _cached = true;
            try
            {
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType("StudentItemWeights", false); } catch { }
                    if (t != null) break;
                }
                if (t == null) return;
                _fCd = t.GetField("RefillCooldownEndsAt", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (_fCd != null)
                {
                    MethodInfo[] ms = _fCd.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
                    for (int i = 0; i < ms.Length; i++)
                        if (ms[i].Name == "Clear" && ms[i].GetParameters().Length == 0) { _mClear = ms[i]; break; }
                }
            }
            catch { }
        }

        public static void ClearCooldown()
        {
            Cache();
            if (_fCd == null) return;
            try
            {
                object d = _fCd.GetValue(null);
                if (d != null && _mClear != null) _mClear.Invoke(d, null);
            }
            catch { }
        }

        public static void Tick()
        {
            if (!On) return;
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 0.5f;
            ClearCooldown();
            Last = _fCd != null ? "已补货" : "字段没找到";
        }

        public static bool Pre_RefillConsumed()
        {
            return !On;   // 开着就跳过原方法 = 不扣补货权重
        }
    }


    // ================= v4.8 秒完成所有互动 + 无限物品 =================
    // v6.5：竹筒（Taser）不走 StudentItemComponent.OnThrown，而是 TaserComponent.TryConsumeShot里
    //   spent = true;  d.CurrentDurability = 0f;   → 两道锁同时上，吹一次就永远吹不了

    // v7.0：本地玩家原地自动旋转（只影响自己的表现）
    public static class AutoSpin
    {
        public static bool On = false;
        public static float Speed = 220f;
        public static string Last = "";
        static float _ang;

        public static void Tick()
        {
            if (!On) return;
            try
            {
                UnitManager um = UnitManager.Instance;
                if (um == null) return;
                Unit me = um.localPlayer;
                if (me == null) return;
                _ang = (_ang + Speed * Time.unscaledDeltaTime) % 360f;
                LocomotionComponent lc = me.GetComponent<LocomotionComponent>();
                if (lc != null) lc.SetRotation(new Vector3(0f, _ang, 0f));
                Transform tr = me.transform;
                if (tr != null) tr.rotation = Quaternion.Euler(0f, _ang, 0f);
                Last = _ang.ToString("0") + "";
            }
            catch { }
        }

        public static void Reset() { _ang = 0f; }
    }

    // v7.0：第三人称 —— 把主相机沿自身 +Z 反方向拉远（在 LateUpdate 里持续覆盖）
    public static class ThirdPerson
    {
        public static bool On = false;
        public static float Distance = 3.4f;
        public static float Height = 0.30f;
        public static string Last = "";
        static bool _hooked;
        static Camera _main;

        /// <summary>v8.4：挂在 Camera.onPreCull —— 这是渲染前最后一个时机，
        /// 游戏的 CameraCtr 在 LateUpdate 里怎么写都会被这里覆盖掉。</summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Camera.onPreCull += OnPreCull;
            Last = "hook ok";
        }

        static int _f = -1;
        static int _cid;

        static void OnPreCull(Camera c)
        {
            if (!On || c == null) return;
            Camera m = Camera.main;
            if (m == null) return;
            if (c != m) return;                     // 只动玩家主相机，别碰 UI / 阴影相机
            // v8.4b: URP 一帧会对同一相机触发多次 onPreCull，不去重就会被推好几次（相机直接穿进墙里）
            if (_f == Time.frameCount && _cid == c.GetInstanceID()) return;
            _f = Time.frameCount; _cid = c.GetInstanceID();

            Transform t = c.transform;
            Vector3 dir = -t.forward;
            float dist = Distance;
            // 后墙检测：别让相机钻进墙/桌子里
            RaycastHit hit;
            if (Physics.Raycast(t.position, dir, out hit, Distance + 0.45f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform != t && !hit.transform.IsChildOf(t))
                    dist = Mathf.Clamp(hit.distance - 0.35f, 0.7f, Distance);
            }
            t.position = t.position + dir * dist + Vector3.up * Height;
            Last = "距离 " + dist.ToString("0.0");
        }

        /// <summary>v8.4：位移只由 onPreCull 做（每帧一次）。这里不再叠加，
        /// 否则会和 onPreCull 双重推导致相机越飘越远/抖动。</summary>
        public static void LateTick()
        {
            if (!On) return;
            if (!_hooked) Hook();
        }
    }

    public static class Instant
    {
        public static bool On = true;        // 秒完成（写板书/吃东西/吹笛子/解救/取货/答题）
        public static bool InfItems = true;  // 无限物品（耐久永远满）
        public static string Last = "";
        static float _next;
        static FieldInfo _fRelease;    // StudentInterectComponent._releaseTime
        static FieldInfo _fBlack;      // BlackBoardComponent.writingProgress
        static FieldInfo _fItemData;   // StudentItemComponent.itemData
        static FieldInfo _fDur;        // ItemData.Durability
        static bool _cached;

        static void Cache()
        {
            if (_cached) return;
            _cached = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try { _fRelease = typeof(StudentInterectComponent).GetField("_releaseTime", F); } catch { }
            try { _fBlack = typeof(BlackBoardComponent).GetField("writingProgress", F); } catch { }
            try { _fItemData = typeof(StudentItemComponent).GetField("itemData", F); } catch { }
            if (_fItemData != null)
            {
                try { _fDur = _fItemData.FieldType.GetField("CurrentDurability", F); } catch { }
                if (_fDur == null) { try { _fDur = _fItemData.FieldType.GetField("Durability", F); } catch { } }
            }
        }

        // 把一个物品单位的耐久顶满
        static void FillDur(Unit item)
        {
            if (item == null || _fItemData == null || _fDur == null) return;
            try
            {
                var sic = item.GetComponent<StudentItemComponent>();
                if (sic == null) return;
                object data = _fItemData.GetValue(sic);
                if (data == null) return;
                _fDur.SetValue(data, 9999f);
            }
            catch { }
        }

        // v6.2：竹筒（Taser）配置里 Durability 只有 1，用一次就扣到 0。
        // ReduceItemDurability 里就是 itemData.CurrentDurability -= 1f，直接跳过。
        static int _diagItems;

        // v6.4：OnThrown 里 CompletesOnThrow 默认为 true（completionRule 为 null）→ 抛出去就“用完”、发 ItemUseCompleted_C2S 让服务端销毁
        public static bool Pre_OnThrown(StudentItemComponent __instance, Unit __0)
        {
            if (!InfItems) return true;
            if (_diagItems < 6)
            {
                _diagItems++;
                string iu = "?";
                try { if (__instance != null && __instance.owner != null) iu = __instance.owner.UID.ToString(); } catch { }
                HackPlugin.Log("[WceHack] 拦住抛掷完成 item=" + iu);
            }
            return false;
        }

        public static bool Pre_ReduceDur(StudentItemComponent __instance)
        {
            if (!InfItems) return true;
            try { __instance.itemData.CurrentDurability = 9999f; } catch { }
            if (_diagItems < 3)
            {
                _diagItems++;
                HackPlugin.Log("[WceHack] 拦住扣耐久");
            }
            return false;
        }

        // 投出去也不进入已用完状态，否则物品会被回收掉
        public static bool Pre_NotifyCompleted(StudentItemComponent __instance, Unit __0)
        {
            if (!InfItems) return true;
            // v6.4：旧版本在此判 __instance.holder，而抛掷那一瞬 holder 已被清空 → 直接放行了
            Last = "物品不消耗";
            if (_diagItems < 9)
            {
                _diagItems++;
                string iu = "?";
                try { if (__instance != null && __instance.owner != null) iu = __instance.owner.UID.ToString(); } catch { }
                HackPlugin.Log("[WceHack] 拦住物品完成 item=" + iu);
            }
            return false;
        }

        // v6.3：ApplyThrown 里 pickupBlockedByThrow = (不是答题卡) → 竹筒扔出去就永远捡不回来
        public static void Post_ApplyThrown(StudentItemComponent __instance)
        {
            if (!InfItems || __instance == null) return;
            CacheItemFields();
            try { _fPickupBlocked.SetValue(__instance, false); } catch { }
            try { _fUseDone.SetValue(__instance, false); } catch { }
        }

        static FieldInfo _fUseDone, _fPickupBlocked;
        static bool _fCached;
        static void CacheItemFields()
        {
            if (_fCached) return;
            _fCached = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try { _fUseDone = typeof(StudentItemComponent).GetField("useCompleted", F); } catch { }
            try { _fPickupBlocked = typeof(StudentItemComponent).GetField("pickupBlockedByThrow", F); } catch { }
        }

        // 持续保活：手上/嘴里那件物品，耐久顶满 + 不处于已用完 + 不禁止拾取
        public static void KeepAlive(Unit itemUnit)
        {
            if (!InfItems || itemUnit == null) return;
            CacheItemFields();
            try
            {
                StudentItemComponent sic = itemUnit.GetComponent<StudentItemComponent>();
                if (sic == null) return;
                sic.itemData.CurrentDurability = 9999f;
                if (_fUseDone != null) _fUseDone.SetValue(sic, false);
                if (_fPickupBlocked != null) _fPickupBlocked.SetValue(sic, false);
            }
            catch { }
        }

        public static void Tick()
        {
            if (!On && !InfItems) return;
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 0.03f;
            Cache();

            var um = Aim.UM();
            if (um == null) return;
            Unit me = null;
            try { me = um.localPlayer; } catch { }
            if (me == null) return;

            // 1) 嘴上的物品（吹笛子 / 气球 / 泡泡糖 / 吃东西）
            try
            {
                var mo = me.GetComponent<MouthItemUseComponent>();
                if (mo != null)
                {
                    if (On && mo._isHoldingUse)
                    {
                        float t0 = mo._targetDura > 0f ? mo._targetDura : 999f;
                        if (t0 < 999f) t0 = 999f;
                        mo._useDura = t0;
                        Last = "嘴上物品秒完成";
                    }
                    if (InfItems) { FillDur(mo.currentMouthItem); KeepAlive(mo.currentMouthItem); }
                }
            }
            catch { }

            // 2) 手上的物品（使用 / 吃 / 喝）
            try
            {
                var su = me.GetComponent<StudentItemUseComponent>();
                if (su != null)
                {
                    if (On && su._isHoldingUse)
                    {
                        float t1 = su._targetDura > 0f ? su._targetDura : 999f;
                        if (t1 < 999f) t1 = 999f;
                        su._useDura = t1;
                        Last = "手上物品秒完成";
                    }
                    if (InfItems) { FillDur(su.currentHoldUnit); KeepAlive(su.currentHoldUnit); }
                }
            }
            catch { }

            // 3) 解救同伴（按住读条的那种）
            if (On && _fRelease != null)
            {
                try
                {
                    var si = me.GetComponent<StudentInterectComponent>();
                    if (si != null)
                    {
                        object v = _fRelease.GetValue(si);
                        if (v is float && (float)v >= 0f && (float)v < 500f)
                        {
                            _fRelease.SetValue(si, 9999f);
                            Last = "解救秒完成";
                        }
                    }
                }
                catch { }
            }

            // 4) 写板书进度
            if (On && _fBlack != null)
            {
                try
                {
                    var bb = me.GetComponent<BlackBoardComponent>() ;
                    if (bb == null)
                    {
                        // 板书组件在黑板实体上，找最近的
                        var keys = new List<ulong>(um.unitsDict.Keys);
                        for (int i = 0; i < keys.Count; i++)
                        {
                            Unit u = null;
                            try { u = um.unitsDict[keys[i]]; } catch { }
                            if (u == null) continue;
                            var c = u.GetComponent<BlackBoardComponent>();
                            if (c == null) continue;
                            bb = c; break;
                        }
                    }
                    if (bb != null)
                    {
                        object v = _fBlack.GetValue(bb);
                        if (v is float && (float)v < 0.99f && (float)v > 0f)
                        {
                            _fBlack.SetValue(bb, 1f);
                            Last = "板书秒写完";
                        }
                    }
                }
                catch { }
            }

            // 5) 无限物品：全场扫一遍，把所有物品耐久顶满
            if (InfItems)
            {
                try
                {
                    var keys = new List<ulong>(um.unitsDict.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        Unit u = null;
                        try { u = um.unitsDict[keys[i]]; } catch { }
                        if (u == null) continue;
                        FillDur(u);
                    }
                }
                catch { }
            }
        }
    }

    // ---------- 自动脱困（学生：眩晕 / 被老师抓 / 被绑惩罚柱） ----------
    public static class Escape
    {
        public static bool Auto = false;  // v5.5: 默认关。自动脱困已并入 Protect（它只清状态、不钉位置，不会卡住）

        public static string DoEscapeOnce()
        {
            var um = Aim.UM();
            if (um == null) return "没有本地玩家";
            Unit me = null;
            try { me = um.localPlayer; } catch { }
            if (me == null) return "没有本地玩家";
            bool ok = false;
            try { ok = DoEscape(me); } catch { }
            if (!ok) { Last = "当前没被困"; return "当前没被困"; }
            return "已脱困 " + Last;
        }
        public static float Interval = 0.2f;
        public static string Last = "";
        static float _next;

        public static void Tick()
        {
            if (!Auto) return;
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + Interval;
            var um = Aim.UM();
            if (um == null) return;
            Unit me = null;
            try { me = um.localPlayer; } catch { }
            if (me == null) return;
            try { if (me.IsDead) return; } catch { }
            DoEscape(me);
        }

        public static bool DoEscape(Unit me)
        {
            bool acted = false;
            string why = "";

            // 1) 眩晕：v2.2 起不再自动 InterruptStun（会让状态机反复切、表现为一直滑行）
            //    眩晕由 Protect.NoStun 从源头拦掉

            // 2) 被老师拎起来
            try
            {
                var bc = me.GetComponent<BeCatchedComponent>();
                if (bc != null)
                {
                    // v6.0：不看本地状态（拦了 OnGetCatched 它就恒为 0），无条件扫全场谁拿着我
                    if (true)
                    {
                        // v4.3：删掉"把位置钉回 keep"那两行 —— 那就是飞天的元凶。
                        // 被抓的那一帧 me.Position 已经是老师手上/空中的坐标，
                        // 钉住 = 直接把玩家钉在半空（会一路飘到大空地）。
                        // 现在只清状态，位置交给 Safe.Tick 做健全检查。

                        var um2 = Aim.UM();
                        if (um2 != null && um2.unitsDict != null)
                        {
                            var keys = new List<ulong>(um2.unitsDict.Keys);
                            for (int i = 0; i < keys.Count; i++)
                            {
                                Unit other = null;
                                try { other = um2.unitsDict[keys[i]]; } catch { }
                                if (other == null || other == me) continue;
                                var cc = other.GetComponent<CatchComponent>();
                                if (cc == null) continue;
                                Unit held = null;
                                try { held = cc.catchedUnit; } catch { }
                                if (held != me) continue;
                                try { cc.ReleaseUnit(true, true); acted = true; why += "老师放手 "; } catch { }
                            }
                        }

                        // v5.5：不再调 ClearMountingPos —— 它会让服务端重算落点，配合其它逻辑就"卡住"
                        try { bc.OnGetReleased(null, null); acted = true; why += "解抓 "; } catch { }
                    }
                }
            }
            catch { }

            // 3) 被绑在惩罚柱上
            try
            {
                var um = Aim.UM();
                if (um != null && um.unitsDict != null)
                {
                    ulong my = 0;
                    try { my = me.UID; } catch { }
                    foreach (var kv in um.unitsDict)
                    {
                        var u = kv.Value;
                        if (u == null) continue;
                        var pillar = u.GetComponent<PunishPillarComponent>();
                        if (pillar == null) continue;
                        ulong bound = 0;
                        try { bound = pillar.BoundStudentUID; } catch { continue; }
                        if (bound != my) continue;
                        try { pillar.ReleaseStudent(); } catch { }
                        try { pillar.OnResponseReleaseStudent(); } catch { }
                        acted = true;
                        why += "柱子 ";
                    }
                }
            }
            catch { }

            if (!acted) return false;

            // v1.7：不再无条件传送（以前 BornPosition 可能是地图外的坐标，会把人甩出地图）。
            // 只在真的掉出地图时才救回来，并且贴地检测过才落点。
            try
            {
                Vector3 cur = me.Position;
                if (cur.y < -8f || cur.y > 300f)
                {
                    Vector3 safe = me.BornPosition;
                    RaycastHit hit;
                    bool ok = false;
                    try
                    {
                        if (Physics.Raycast(safe + Vector3.up * 8f, Vector3.down, out hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            safe = hit.point + Vector3.up * 0.3f;
                            ok = true;
                        }
                    }
                    catch { }
                    if (!ok)
                    {
                        try
                        {
                            if (Physics.Raycast(cur + Vector3.up * 8f, Vector3.down, out hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                                safe = hit.point + Vector3.up * 0.3f;
                        }
                        catch { }
                    }
                    try { me.SetPosition(safe); } catch { }
                    try
                    {
                        var loc = me.GetComponent<LocomotionComponent>();
                        if (loc != null) loc.SetPosition(safe);
                    }
                    catch { }
                    HackPlugin.Log("脱困救回落地：" + safe.x.ToString("0.0") + "," + safe.y.ToString("0.0") + "," + safe.z.ToString("0.0"));
                }
            }
            catch { }

            Last = why.Trim();
            HackPlugin.Log("脱困：" + Last);
            return true;
        }
    }

    // ---------- 完全隐形（网络状态 + 渲染彻底抹掉） ----------
    // ---------- 无限体力（v1.6，只对自己）----------
    // ---------- 强制变老师（本地视角） ----------
    // ================= 穿墙：粉笔 / 黑板擦 / 剑气 无视墙体 =================
    // ConstantDefine.ChalkColliderMask 原本是 ~(Interaction|Player|PlayerSelf)，
    // 也就是 Wall / Ground / Obstacle / Door 全在里面 → 粉笔撞墙就停。
    // TeacherSlashProjectile 另有三个 static mask：EnvironmentMask（剑气撞环境停）、
    // EraserEnvironmentMask（黑板擦撞环境停）、EraserCollisionMask（黑板擦找目标）。
    // 把环境 mask 清零、目标 mask 只留角色层，就打穿墙了。
    public static class WallHack
    {
        public static bool On = true;
        public static string Last = "";
        static int _mask = -1;
        static bool _applied;
        static object _oldChalk, _oldEnv, _oldEEnv, _oldECol;
        static bool _saved;

        public static int CharMask()
        {
            if (_mask < 0)
            {
                try { _mask = LayerMask.GetMask(new string[] { "Interaction", "Player", "PlayerSelf", "HitBox" }); }
                catch { _mask = 0; }
            }
            return _mask;
        }

        static FieldInfo F(Type t, string n)
        {
            return t.GetField(n, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        static bool Set(Type t, string field, object val, ref string err)
        {
            try
            {
                var f = F(t, field);
                if (f == null) { err += field + "缺 "; return false; }
                if (!_saved) { } // 保存交给 Apply 统一做
                f.SetValue(null, val);
                return true;
            }
            catch (Exception e) { err += field + "=" + e.Message + " "; return false; }
        }

        public static void Apply()
        {
            string err = "";
            int cm = CharMask();
            var tp = typeof(Unit).Assembly.GetType("TeacherSlashProjectile");

            if (!_saved)
            {
                _saved = true;
                try { var f = F(typeof(ConstantDefine), "ChalkColliderMask"); if (f != null) _oldChalk = f.GetValue(null); } catch { }
                if (tp != null)
                {
                    try { var f1 = F(tp, "EnvironmentMask"); if (f1 != null) _oldEnv = f1.GetValue(null); } catch { }
                    try { var f2 = F(tp, "EraserEnvironmentMask"); if (f2 != null) _oldEEnv = f2.GetValue(null); } catch { }
                    try { var f3 = F(tp, "EraserCollisionMask"); if (f3 != null) _oldECol = f3.GetValue(null); } catch { }
                }
            }

            Set(typeof(ConstantDefine), "ChalkColliderMask", (object)(LayerMask)cm, ref err);
            if (tp != null)
            {
                Set(tp, "EnvironmentMask", (object)0, ref err);
                Set(tp, "EraserEnvironmentMask", (object)0, ref err);
                Set(tp, "EraserCollisionMask", (object)cm, ref err);
            }
            else err += "TeacherSlashProjectile缺 ";
            _applied = true;
            Last = err.Length == 0 ? ("穿墙开 mask=" + cm) : ("部分失败 " + err);
            HackPlugin.Log("[WceHack] " + Last);
        }

        public static void Restore()
        {
            var tp = typeof(Unit).Assembly.GetType("TeacherSlashProjectile");
            try { var f = F(typeof(ConstantDefine), "ChalkColliderMask"); if (f != null && _oldChalk != null) f.SetValue(null, _oldChalk); } catch { }
            if (tp != null)
            {
                try { var f1 = F(tp, "EnvironmentMask"); if (f1 != null && _oldEnv != null) f1.SetValue(null, _oldEnv); } catch { }
                try { var f2 = F(tp, "EraserEnvironmentMask"); if (f2 != null && _oldEEnv != null) f2.SetValue(null, _oldEEnv); } catch { }
                try { var f3 = F(tp, "EraserCollisionMask"); if (f3 != null && _oldECol != null) f3.SetValue(null, _oldECol); } catch { }
            }
            _applied = false;
            Last = "穿墙关";
            HackPlugin.Log("[WceHack] 穿墙已恢复原 mask");
        }

        public static void Tick()
        {
            if (On) { if (!_applied) Apply(); }
            else if (_applied) Restore();
        }

        // ProjectileFlightQuery(int mask, TargetFilter accepts, BoxCollider box) —— 黑板擦/剑气的飞行检测
        public static void Pre_FlightMask(ref int mask)
        {
            if (On) mask = CharMask();
        }
    }

    public static class Teacher
    {
        public static string Last = "";
        public static bool Done = false;

        // IsTeacher 的唯一判据是 UnitTable.SubType == 101，
        // 把 RoleData.UnitTable 换成一份改过 SubType 的克隆即可（不动共享表），
        // 再把老师专属组件补上：SkillComponent（粉笔/技能）、TeacherSprintComponent（冲刺）、
        // TeacherStunComponent、TeacherExhaustionComponent、TeacherMeleeComponent（剑气）。
        static void Log(string s) { Last = s; HackPlugin.Log("[WceHack] " + s); }

        public static string Apply(Unit me)
        {
            if (me == null) { Log("没有本地玩家"); return Last; }
            try { if (me.IsTeacher) { Log("已经是老师了 SubType=" + (int)me.UnitSubType); return Last; } } catch { }

            RoleData rd = null;
            try { rd = me.GetData<RoleData>(); } catch (Exception e) { Log("GetData 失败 " + e.Message); return Last; }
            if (rd == null) { Log("拿不到 RoleData"); return Last; }
            if (rd.UnitTable == null) { Log("UnitTable 为空"); return Last; }

            cfg.Unit target = null;
            string how = "";

            // ① 首选：配置表里本来就有老师单位，直接借来用 —— 不写任何 readonly 字段
            try
            {
                cfg.TbUnit tb = cfg.Tables.TbUnit;
                if (tb != null)
                {
                    var list = tb.DataList;
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            cfg.Unit u = list[i];
                            if (u == null) continue;
                            if ((int)u.SubType == 101) { target = u; how = "表 id=" + u.Id; break; }
                        }
                    }
                }
            }
            catch (Exception e) { Log("查表异常 " + e.Message); }

            // ② 兜底：克隆自己那份配置，反射改 SubType / Type（Mono 下 initonly 可能写不进去）
            if (target == null)
            {
                try
                {
                    var mi = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
                    cfg.Unit clone = (cfg.Unit)mi.Invoke(rd.UnitTable, null);
                    const BindingFlags FF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var fSub = typeof(cfg.Unit).GetField("SubType", FF);
                    var fTyp = typeof(cfg.Unit).GetField("Type", FF);
                    if (fSub == null) { Log("找不到 SubType 字段"); return Last; }
                    fSub.SetValue(clone, 101);
                    if (fTyp != null) fTyp.SetValue(clone, 1);
                    target = clone; how = "克隆";
                }
                catch (Exception e) { Log("克隆/改配置失败 " + e.Message); return Last; }
            }

            if (target == null) { Log("没有可用的老师配置"); return Last; }
            try { rd.UnitTable = target; }
            catch (Exception e) { Log("写 UnitTable 失败 " + e.Message); return Last; }

            int added = 0;
            added += AddComp<SkillComponent>(me);
            added += AddComp<TeacherSprintComponent>(me);
            added += AddComp<TeacherStunComponent>(me);
            added += AddComp<TeacherExhaustionComponent>(me);
            added += AddComp<TeacherMeleeComponent>(me);
            added += AddComp<PlayerSpeedBoostComponent>(me);

            Done = true;
            bool isT = false;
            try { isT = me.IsTeacher; } catch { }
            Log("已变老师(" + how + ") IsTeacher=" + isT + " 补组件+" + added);
            return Last;
        }

        static int AddComp<T>(Unit me) where T : class, IUnitComponent, new()
        {
            try
            {
                if (me.GetComponent<T>() != null) return 0;
                T c = ObjectManager.comPools.Get<T>();
                if (c == null) return 0;
                me.AddComponent(c);
                HackPlugin.Log("[WceHack] add " + typeof(T).Name);
                return 1;
            }
            catch (Exception e)
            {
                HackPlugin.Log("[WceHack] add " + typeof(T).Name + " failed: " + e.Message);
                return 0;
            }
        }
    }

    public static class Energy
    {
        public static bool On = true;
        public static string Last = "";
        static float _t;

        public static void Tick()
        {
            if (!On) return;
            float now = Time.unscaledTime;
            if (now - _t < 0.2f) return;
            _t = now;
            try
            {
                var um = UnitManager.Instance;
                if (um == null) return;
                var me = um.localPlayer;
                if (me == null) return;

                // 技能能量
                var sk = me.GetComponent<SkillComponent>();
                if (sk != null)
                {
                    sk.currentSkillEnergy = 9999f;
                    Last = "技能能量 " + sk.currentSkillEnergy.ToString("0");
                }
                // 老师疲劳
                var ex = me.GetComponent<TeacherExhaustionComponent>();
                if (ex != null) ex.Apply(false);
            }
            catch { }
        }
    }


    // ================= 视角锁定：眩晕镜头永不播放 =================
    public static class StableCam
    {
        public static bool On = true;
        public static string Last = "";
        static float _next;
        static FieldInfo _fActive;

        // CameraComponent.PlayStunHitCamera() 是眩晕抖镜头的唯一入口；prefix 返回 false 就不播
        public static bool Pre_StunCam(CameraComponent __instance)
        {
            if (!On) return true;
            Last = "已拦眩晕镜头";
            return false;
        }

        // Unit.IsCameraLimited 为 true 时视角会被限制/拉扯；一律返回 false
        public static void Post_NotLimited(ref bool __result)
        {
            if (On) __result = false;
        }

        // 被抓时：相机切锚点 / 姿态插值 / 跟随切换 全部拦掉，视角一动不动
        public static bool Pre_NoCatchCam(CameraComponent __instance)
        {
            if (!On) return true;
            try
            {
                Unit o = __instance.owner;
                if (o == null) return true;
                bool caught = false;
                try { caught = o.IsPicked; } catch { }
                if (!caught)
                {
                    try { caught = o.CurrentCatchUnit != null; } catch { }
                }
                if (!caught)
                {
                    var bc = o.GetComponent<BeCatchedComponent>();
                    if (bc != null) { try { caught = bc.catcher != null || bc.CapturingTeacherUID != 0; } catch { } }
                }
                if (caught) { Last = "被抓：视角锁死"; return false; }
            }
            catch { }
            return true;
        }

        public static void Tick()
        {
            if (!On) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            UnitManager um = Aim.UM(); if (um == null) return;
            Unit me = null; try { me = um.localPlayer; } catch { }
            if (me == null) return;
            try
            {
                CameraComponent cc = me.GetComponent<CameraComponent>();
                if (cc == null) return;
                if (_fActive == null) _fActive = typeof(CameraComponent).GetField("_stunHitCameraActive",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (_fActive != null && (bool)_fActive.GetValue(cc)) _fActive.SetValue(cc, false);
            }
            catch { }
        }
    }

    // ================= v5.9 抽屉塞竹筒 =================

    // ================= v5.7 刷新人物状态（把被抓/眩晕/挂载全部强制清掉） =================
    public static class Refresh
    {
        public static bool On = true;      // 自动刷新，每 0.08 秒一次
        public static string Last = "";
        static float _next;
        static FieldInfo _fStunTime, _fRecovery, _fRecoverying, _fProjStun;
        static FieldInfo _fCatcher, _fCapUID;
        static bool _cached;

        static void Cache()
        {
            if (_cached) return;
            _cached = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            try
            {
                Type t = typeof(StudentStunComponent);
                _fStunTime = t.GetField("stunTime", F);
                _fRecovery = t.GetField("recoveryTime", F);
                _fRecoverying = t.GetField("isRecovery", F);
                _fProjStun = t.GetField("projectileStun", F);
            }
            catch { }
            try
            {
                Type b = typeof(BeCatchedComponent);
                _fCatcher = b.GetField("catcher", F);
                _fCapUID = b.GetField("<CapturingTeacherUID>k__BackingField", F);
            }
            catch { }
        }

        public static void Do()
        {
            Unit me = null;
            try { var um = Aim.UM(); if (um != null) me = um.localPlayer; } catch { }
            if (me == null) { Last = "没有本地玩家"; return; }
            Do(me);
        }

        public static void Do(Unit me)
        {
            if (me == null) return;
            Cache();
            int n = 0;

            // 1) 眩晕状态 + 眩晕计时器
            try
            {
                var st = me.GetComponent<StudentStunComponent>();
                if (st != null)
                {
                    if (st.isStuned) { st.isStuned = false; n++; }
                    try { if (_fStunTime != null) _fStunTime.SetValue(st, 0f); } catch { }
                    try { if (_fRecovery != null) _fRecovery.SetValue(st, 0f); } catch { }
                    try { if (_fRecoverying != null) _fRecoverying.SetValue(st, false); } catch { }
                    try { if (_fProjStun != null) _fProjStun.SetValue(st, false); } catch { }
                }
            }
            catch { }

            // 2) 被老师抓着
            try
            {
                var bc = me.GetComponent<BeCatchedComponent>();
                if (bc != null)
                {
                    bool held = false;
                    try { held = bc.catcher != null || bc.CapturingTeacherUID != 0UL; } catch { }
                    if (held)
                    {
                        try { bc.OnGetReleased(null, null); } catch { }
                        try { if (_fCatcher != null) _fCatcher.SetValue(bc, null); } catch { }
                        try { if (_fCapUID != null) _fCapUID.SetValue(bc, 0UL); } catch { }
                        n++;
                    }
                }
            }
            catch { }

            // 3) 我手里抱着别人 → 放掉，别把自己卡住
            try
            {
                var cc = me.GetComponent<CatchComponent>();
                if (cc != null && cc.catchedUnit != null) { cc.ReleaseUnit(true, false); n++; }
            }
            catch { }

            // 4) 挂载点（只清，不重设位置，所以不会滑走）
            try { me.ClearMountingPos(false, false); } catch { }

            if (n > 0) Last = "已刷新(" + n + ")";
        }

        public static void Tick()
        {
            if (!On) return;
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + 0.08f;
            Do();
        }
    }

    // ================= 不可被抓 / 不可被打晕 =================
    // v6.1：惩罚柱彻底解绑
    // 根因：UnitStateStudentPunished.CanChange() 对本地玩家直接 return owner.releaseFromPillar，
    // 而服务端 SkillMessageHandler.OnStudentReleaseMessageRequest 会拒绝学生自己的解柱请求
    // → releaseFromPillar 永远是 false → 状态机锁死在 103，动画一直循环。
    // 另外 UnitStateStudentPunished.Update 每 1 秒按 NetData.MountingPointNet 把位置拉回柱子。
    public static class Pillar
    {
        public static bool On = true;
        public static string Last = "";
        static float _next;
        static FieldInfo _fBound, _fBind, _fVer;
        static bool _cached;

        static void Cache()
        {
            if (_cached) return;
            _cached = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try { _fBound = typeof(PunishPillarComponent).GetField("<BoundStudentUID>k__BackingField", F); } catch { }
            try { _fBind = typeof(PunishPillarComponent).GetField("<bindStudent>k__BackingField", F); } catch { }
            try { _fVer = typeof(PunishPillarComponent).GetField("<BindingVersion>k__BackingField", F); } catch { }
        }

        public static void Tick()
        {
            if (!On) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.10f;
            UnitManager um = Aim.UM(); if (um == null) return;
            Unit me = null; try { me = um.localPlayer; } catch { }
            if (me == null) return;
            try { if (me.IsDead) return; } catch { }
            try { Do(me); } catch { }
        }

        // 强制解柱：不管服务端同不同意，本地把状态机、挂载点、柱子绑定全部清掉
        public static bool Do(Unit me)
        {
            if (me == null) return false;
            Cache();
            bool acted = false;
            int st = 0; try { st = me.CurState(); } catch { }
            bool punished = (st == 103);
            ulong my = 0; try { my = me.UID; } catch { }

            // 1) 找绑我的柱子，清绑定
            try
            {
                var um = Aim.UM();
                if (um != null && um.unitsDict != null)
                {
                    var keys = new List<ulong>(um.unitsDict.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        Unit u = null;
                        try { u = um.unitsDict[keys[i]]; } catch { }
                        if (u == null) continue;
                        PunishPillarComponent p = null;
                        try { p = u.GetComponent<PunishPillarComponent>(); } catch { }
                        if (p == null) continue;
                        ulong bound = 0;
                        try { bound = p.BoundStudentUID; } catch { }
                        Unit bs = null;
                        try { bs = p.bindStudent; } catch { }
                        bool mine = (bound != 0UL && bound == my) || (bs != null && bs == me);
                        if (!mine) continue;
                        if (_fBound != null) { try { _fBound.SetValue(p, 0UL); } catch { } }
                        if (_fBind != null) { try { _fBind.SetValue(p, null); } catch { } }
                        if (_fVer != null) { try { _fVer.SetValue(p, p.BindingVersion + 1U); } catch { } }
                        acted = true;
                        punished = true;
                    }
                }
            }
            catch { }

            if (!punished && !acted) return false;

            // 2) 解锁状态机：CanChange() 对本地玩家直接看这个字段
            try { me.releaseFromPillar = true; acted = true; } catch { }

            // 3) 断开位置拉回：Update 每 1 秒看 NetData.MountingPointNet.HaveValue
            try
            {
                RoleData rd = me.GetData<RoleData>();
                if (rd != null)
                {
                    rd.NetData.MountingPointNet.HaveValue = false;
                    rd.NetData.MountingPointNet.MountingUID = 0UL;
                }
            }
            catch { }

            // 4) 清挂载（第二个参数 true = 通知服务端，否则它下次同步又把人拽回去）
            // v6.3：第二个参数 true 会让服务端把人重定位到 createPosition（建筑顶）→ 改 false
            try { me.ClearMountingPos(true, false); acted = true; } catch { }

            // 5) 强制把状态机切回 Idle（柱子动画就是卡在 103 导致的）
            try
            {
                if (me.CurState() == 103)
                {
                    me.releaseFromPillar = true;
                    FSMChangeStateParam prm = default(FSMChangeStateParam);
                    me.ChangeState(EUnitFsmStateType.PlayerIdle, prm);
                }
            }
            catch { }

            // 6) 落地：柱子四周找地面
            try
            {
                Vector3 p0 = me.Position;
                Vector3 safe = p0;
                // v6.3：先就近向下打，且落点不允许高于当前位置 1.5 米（否则会从天花板上方打中楼顶）
                RaycastHit hNear;
                if (Physics.Raycast(p0 + Vector3.up * 0.6f, Vector3.down, out hNear, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Vector3 cand = hNear.point + Vector3.up * 0.35f;
                    if (cand.y <= p0.y + 1.5f) safe = cand;
                }
                Vector3[] dirs = new Vector3[] {
                    new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f),
                    new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f)
                };
                bool ok = false;
                for (int i = 0; i < dirs.Length; i++)
                {
                    Vector3 probe = p0 + dirs[i] * 1.6f + Vector3.up * 3f;
                    RaycastHit hit;
                    if (Physics.Raycast(probe, Vector3.down, out hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        safe = hit.point + Vector3.up * 0.35f;
                        ok = true;
                        break;
                    }
                }
                if (!ok)
                {
                    RaycastHit h2;
                    if (Physics.Raycast(p0 + Vector3.up * 6f, Vector3.down, out h2, 40f, ~0, QueryTriggerInteraction.Ignore))
                        safe = h2.point + Vector3.up * 0.35f;
                    else
                        safe = p0 + Vector3.up * 1.2f;
                }
                me.SetPosition(safe);
                LocomotionComponent lc = null;
                try { lc = me.GetComponent<LocomotionComponent>(); } catch { }
                if (lc != null) { try { lc.SetPosition(safe); } catch { } }
                try { me.transform.position = safe; } catch { }
            }
            catch { }

            Last = "强制解柱（状态 " + st + " → Idle）";
            return true;
        }
    }

    public static class Protect
    {
        public static bool NoCatch = true;
        public static bool NoStun = true;
        public static string Last = "";
        static float _next;
        static int _logged = 3;

        // 老师抓学生的唯一入口；prefix 返回 false 直接跳过原方法
        public static bool Pre_CatchUnit(CatchComponent __instance, Unit __0)
        {
            if (!NoCatch || __0 == null) return true;
            bool isMe = false;
            try { isMe = __0.isLocalPlayer; } catch { }
            if (!isMe) return true;
            if (_logged > 0) { _logged--; HackPlugin.Log("[WceHack] 拦截被抓 uid=" + __0.UID); }
            return false;
        }

        // v5.7：眩晕的视觉特效入口（屏幕扭曲/重影/黑边都在这里），直接跳过
        public static bool Pre_DealStunEffect(StudentStunComponent __instance, EStunType __0, float __1)
        {
            if (!NoStun) return true;
            Unit o = null;
            try { o = __instance != null ? __instance.owner : null; } catch { }
            if (o == null) return true;
            bool isMe = false;
            try { isMe = o.isLocalPlayer; } catch { }
            if (!isMe) return true;
            Last = "已挡眩晕特效";
            return false;
        }

        // v5.7：真正进入眩晕状态的方法，也挡掉
        public static bool Pre_ApplyStun(StudentStunComponent __instance, float __0, float __1)
        {
            if (!NoStun) return true;
            Unit o = null;
            try { o = __instance != null ? __instance.owner : null; } catch { }
            if (o == null) return true;
            bool isMe = false;
            try { isMe = o.isLocalPlayer; } catch { }
            if (!isMe) return true;
            return false;
        }

        // v5.4：被抓住时的本地回调，直接拦掉（这是比 CatchUnit 更靠得住的点）
        public static bool Pre_OnGetCatched(BeCatchedComponent __instance, Unit __0)
        {
            if (!NoCatch) return true;
            Unit o = null;
            try { o = __instance != null ? __instance.owner : null; } catch { }
            if (o == null) return true;
            bool isMe = false;
            try { isMe = o.isLocalPlayer; } catch { }
            if (!isMe) return true;
            if (_logged > 0) { _logged--; HackPlugin.Log("[WceHack] 拦截被抓住 OnGetCatched uid=" + o.UID); }
            Last = "刚拦住一次抓取";
            return false;
        }

        // 被打晕（投掷物/黑板擦命中）
        public static bool Pre_Stun(StudentStunComponent __instance, float __0, ref bool __result)
        {
            if (!NoStun) return true;
            Unit o = null;
            try { o = __instance.owner; } catch { }
            if (o == null) return true;
            bool isMe = false;
            try { isMe = o.isLocalPlayer; } catch { }
            if (!isMe) return true;
            __result = false;
            return false;
        }

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.12f;
            UnitManager um = Aim.UM(); if (um == null) return;
            Unit me = null; try { me = um.localPlayer; } catch { }
            if (me == null) return;

            if (NoStun)
            {
                try
                {
                    StudentStunComponent st = me.GetComponent<StudentStunComponent>();
                    if (st != null && st.isStuned) st.isStuned = false;
                }
                catch { }
            }

            // v5.4：已经被拎起来了 → 立刻放下，并把坐标钉回当帧位置（不重设挂载点，所以不会滑行）
            if (NoCatch)
            {
                try
                {
                    BeCatchedComponent bc = me.GetComponent<BeCatchedComponent>();
                    // v6.0：不再看本地 held —— v5.4 拦了 OnGetCatched，catcher/CapturingTeacherUID 恒为空，
                    // 导致「已被拿住」永远检测不到，而服务端那边照样拿着你。现在无条件扫。
                    if (true)
                    {
                        // v5.5：只清状态，绝不钉位置 —— 每 0.12s 把坐标写回去会让玩家按 WASD 也走不动（卡住）
                        if (bc != null) { try { bc.OnGetReleased(null, null); } catch { } }
                        try
                        {
                            var um2 = Aim.UM();
                            if (um2 != null && um2.unitsDict != null)
                            {
                                var keys = new List<ulong>(um2.unitsDict.Keys);
                                for (int i = 0; i < keys.Count; i++)
                                {
                                    Unit other = null;
                                    try { other = um2.unitsDict[keys[i]]; } catch { }
                                    if (other == null || other == me) continue;
                                    var cc = other.GetComponent<CatchComponent>();
                                    if (cc == null) continue;
                                    Unit heldU = null;
                                    try { heldU = cc.catchedUnit; } catch { }
                                    if (heldU != me) continue;
                                    try { cc.ReleaseUnit(true, true); } catch { }
                                }
                            }
                        }
                        catch { }
                        Last = "已放开";
                    }

                    // 被绑在惩罚柱上也顺手解掉（不动位置，只解绑）
                    try
                    {
                        ulong my = me.UID;
                        var um3 = Aim.UM();
                        if (um3 != null && um3.unitsDict != null)
                        {
                            var ks = new List<ulong>(um3.unitsDict.Keys);
                            for (int i = 0; i < ks.Count; i++)
                            {
                                Unit uu = null;
                                try { uu = um3.unitsDict[ks[i]]; } catch { }
                                if (uu == null) continue;
                                var pil = uu.GetComponent<PunishPillarComponent>();
                                if (pil == null) continue;
                                ulong bound = 0;
                                try { bound = pil.BoundStudentUID; } catch { continue; }
                                if (bound != my) continue;
                                try { pil.ReleaseStudent(); } catch { }
                                try { pil.OnResponseReleaseStudent(); } catch { }
                                Last = "已解柱";
                            }
                        }
                    }
                    catch { }
                }
                catch { }
            }
        }
    }

    // ================= 秒解救同伴 =================
    public static class Rescue
    {
        public static string Last = "";

        // v5.6：救下来之后 1.2 秒内反复纠正，防止服务端把他又拽回柱子上
        static readonly System.Collections.Generic.List<Unit> _pend = new System.Collections.Generic.List<Unit>();
        static readonly System.Collections.Generic.List<Vector3> _pendAt = new System.Collections.Generic.List<Vector3>();
        static readonly System.Collections.Generic.List<float> _pendEnd = new System.Collections.Generic.List<float>();
        static float _pendNext;

        static void Hold(Unit u, Vector3 at)
        {
            if (u == null) return;
            for (int i = 0; i < _pend.Count; i++) if (_pend[i] == u) { _pendAt[i] = at; _pendEnd[i] = Time.realtimeSinceStartup + 1.2f; return; }
            _pend.Add(u); _pendAt.Add(at); _pendEnd.Add(Time.realtimeSinceStartup + 1.2f);
        }

        public static void Tick()
        {
            if (_pend.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            if (now < _pendNext) return;
            _pendNext = now + 0.1f;
            for (int i = _pend.Count - 1; i >= 0; i--)
            {
                Unit u = _pend[i];
                if (u == null || now > _pendEnd[i])
                {
                    _pend.RemoveAt(i); _pendAt.RemoveAt(i); _pendEnd.RemoveAt(i);
                    continue;
                }
                Vector3 at = _pendAt[i];
                try { u.ClearMountingPos(true, true); } catch { }
                try { u.SetPosition(at); } catch { }
                try
                {
                    var lc = u.GetComponent<LocomotionComponent>();
                    if (lc != null) lc.SetPosition(at);
                }
                catch { }
                try
                {
                    var bc = u.GetComponent<BeCatchedComponent>();
                    if (bc != null) bc.OnGetReleased(null, null);
                }
                catch { }
            }
        }

        static FieldInfo _fBound, _fBindStu, _fBindVer;
        static bool _cachedP;

        static void CachePillar()
        {
            if (_cachedP) return;
            _cachedP = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var t = typeof(PunishPillarComponent);
            _fBound = t.GetField("<BoundStudentUID>k__BackingField", F);
            _fBindStu = t.GetField("<bindStudent>k__BackingField", F);
            _fBindVer = t.GetField("<BindingVersion>k__BackingField", F);
        }

        // 从某点上方往下打一条射线找地面，找不到就返回 false
        public static bool GroundY(Vector3 at, out float y)
        {
            y = 0f;
            try
            {
                RaycastHit hit;
                if (Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out hit, 40f, ~0, QueryTriggerInteraction.Ignore))
                {
                    y = hit.point.y;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void Do()
        {
            int n = 0;
            UnitManager um = Aim.UM();
            if (um == null) { Last = "没进对局"; return; }
            System.Collections.Generic.Dictionary<ulong, Unit> dict = null;
            try { dict = um.unitsDict; } catch { }
            if (dict == null) { Last = "拿不到单位表"; return; }
            System.Collections.Generic.List<Unit> all =
                new System.Collections.Generic.List<Unit>(dict.Values);
            for (int i = 0; i < all.Count; i++)
            {
                Unit u = all[i]; if (u == null) continue;

                // 1) 惩罚柱上绑着的 —— 服务端不一定批，所以直接清本地绑定 + 把人挪下柱子
                PunishPillarComponent p = null;
                try { p = u.GetComponent<PunishPillarComponent>(); } catch { }
                if (p != null)
                {
                    ulong bid = 0; try { bid = p.BoundStudentUID; } catch { }
                    if (bid != 0)
                    {
                        Unit pris = null;
                        try { pris = Aim.ByUid(bid); } catch { }
                        Unit holder = null;
                        try { holder = p.bindStudent; } catch { }
                        if (pris == null) pris = holder;

                        // ① 走一遍正规流程（房主时才可能被服务端接受）
                        try { p.ReleaseStudent(); } catch { }
                        try { p.OnResponseReleaseStudent(); } catch { }

                        // ② 强清内部绑定字段（属性 setter 是 private，只能反射写 backing field）
                        try { CachePillar(); } catch { }
                        try { if (_fBound != null) _fBound.SetValue(p, 0UL); } catch { }
                        try { if (_fBindStu != null) _fBindStu.SetValue(p, null); } catch { }
                        try { if (_fBindVer != null) _fBindVer.SetValue(p, (_fBindVer.GetValue(p) is uint) ? (uint)_fBindVer.GetValue(p) + 1u : 1u); } catch { }

                        // ③ 把学生从柱子上摘下来，落到旁边地面（这才是「卡柱子上」的解药）
                        if (pris != null)
                        {
                            // resetScale=true 恢复被绑时改小的缩放；sendMsg=true 让服务端也知道，不然会被拽回去
                            try { pris.ClearMountingPos(true, true); } catch { }
                            try
                            {
                                Vector3 basePos = u.Position;
                                Vector3 at = basePos;
                                float y;
                                bool found = false;

                                // 四个方向各试一次，找最近的一块能站的地板
                                Vector3[] dirs = new Vector3[] {
                                    Vector3.forward, Vector3.back, Vector3.right, Vector3.left
                                };
                                for (int d = 0; d < dirs.Length; d++)
                                {
                                    Vector3 probe = basePos + dirs[d] * 1.5f;
                                    if (GroundY(probe, out y)) { at = probe; at.y = y + 0.45f; found = true; break; }
                                }
                                // 四边都没有就用柱子正上方往下打
                                if (!found && GroundY(basePos, out y)) { at = basePos; at.y = y + 0.45f; found = true; }
                                // 还不行就抬高一米，至少别嵌进柱子模型里
                                if (!found) at = basePos + Vector3.up * 1.2f;

                                pris.SetPosition(at);
                                var lc2 = pris.GetComponent<LocomotionComponent>();
                                if (lc2 != null) lc2.SetPosition(at);
                                try
                                {
                                    var bc2 = pris.GetComponent<BeCatchedComponent>();
                                    if (bc2 != null) bc2.OnGetReleased(null, null);
                                }
                                catch { }
                                Hold(pris, at);   // 1.2 秒内反复纠正，防被拽回
                            }
                            catch (Exception e2) { Last = "摆放失败 " + e2.GetType().Name; }
                        }
                        n++;
                    }
                }

                // 2) 老师手里抓着的
                CatchComponent c = null;
                try { c = u.GetComponent<CatchComponent>(); } catch { }
                if (c != null)
                {
                    Unit cu = null; try { cu = c.catchedUnit; } catch { }
                    if (cu != null)
                    {
                        try
                        {
                            System.Reflection.MethodInfo mi = typeof(CatchComponent).GetMethod(
                                "ReleaseUnit",
                                System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.Public
                                | System.Reflection.BindingFlags.NonPublic);
                            if (mi != null)
                            {
                                mi.Invoke(c, new object[] { true, true });
                                n++;
                            }
                        }
                        catch { }
                    }
                }
            }
            Last = "解救 " + n + " 人";
            HackPlugin.Log("[WceHack] 秒解救 " + Last);
        }
    }

    public static class Ghost
    {
        public static bool On = false;
        public static bool HideRenderers = true;
        public static string Last = "";
        static Unit _cur;
        static float _nextPush;
        static readonly List<Renderer> _hid = new List<Renderer>();
        static readonly List<GameObject> _hidGo = new List<GameObject>();
        static float _rescanAt;
        static bool _scaled;
        static Vector3 _origScale = Vector3.one;
        public static bool Shrink = true;      // 缩到 0.1% 大小（远处/别人屏幕上也几乎看不见）

        public static void Tick()
        {
            var um = Aim.UM();
            Unit me = null;
            if (um != null) { try { me = um.localPlayer; } catch { } }
            if (me == null || !On) { Reset(); return; }
            Apply(me);
        }

        static Transform RootOf(Unit me)
        {
            Transform root = null;
            try { if (me.model != null) root = me.model.transform; } catch { }
            if (root == null) { try { root = me.transform; } catch { } }
            return root;
        }

        static void Apply(Unit me)
        {
            // 1) 网络字段：IsInvisible 就是渲染/判定共用的那个开关
            try
            {
                var rd = me.GetData<RoleData>();
                if (rd != null) rd.NetData.IsInvisible = true;
            }
            catch { }

            // 2) 官方接口：host 直接生效并广播，client 发 C2S 请求
            float now = Time.realtimeSinceStartup;
            if (now >= _nextPush)
            {
                _nextPush = now + 0.12f;
                try
                {
                    var inv = me.GetComponent<PlayerInvisibilityComponent>();
                    if (inv == null)
                    {
                        // 学生默认没有这个组件 —— 这就是"药水隐形不彻底"的根因：补上它
                        try
                        {
                            var pool = ObjectManager.comPools.Get<PlayerInvisibilityComponent>();
                            if (pool != null) { me.AddComponent(pool); inv = pool; HackPlugin.Log("[WceHack] 补 PlayerInvisibilityComponent"); }
                        }
                        catch (Exception ex) { HackPlugin.Log("[WceHack] 补 invis 失败 " + ex.Message); }
                    }
                    if (inv != null) inv.SetInvisible(true);
                }
                catch { }
            }

            // 3) 彻底抹掉本地渲染（连剪影、影子、名字标签一起消失）
            if (!HideRenderers) return;
            if (_cur != me) { RestoreR(); _cur = me; }
            if (Time.unscaledTime < _rescanAt) return;
            _rescanAt = Time.unscaledTime + 0.12f;
            // 名字标签：NameLabelComponent 的 mNameText 单独关掉
            try
            {
                var nl = me.GetComponent<NameLabelComponent>();
                if (nl != null)
                {
                    var f = typeof(NameLabelComponent).GetField("mNameText",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (f != null)
                    {
                        var txt = f.GetValue(nl) as UnityEngine.Component;
                        if (txt != null && txt.gameObject != null && txt.gameObject.activeSelf)
                        {
                            txt.gameObject.SetActive(false);
                            _hidGo.Add(txt.gameObject);
                            HackPlugin.Log("[WceHack] 隐藏名字标签");
                        }
                    }
                }
            }
            catch { }

            try
            {
                var root = RootOf(me);
                if (root == null) return;
                var rs = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    var r = rs[i];
                    if (r == null || !r.enabled) continue;
                    r.enabled = false;
                    _hid.Add(r);
                }
                // 缩到极小：别人的客户端上就算读到了 transform 也几乎看不到，绕路捡了"隐身披风"只半透明的短板
                if (Shrink && !_scaled)
                {
                    _origScale = root.localScale;
                    root.localScale = new Vector3(0.001f, 0.001f, 0.001f);
                    _scaled = true;
                }
                Last = "隐藏渲染器 " + _hid.Count + " 个";
            }
            catch { }
        }

        static void RestoreR()
        {
            for (int i = 0; i < _hid.Count; i++)
            {
                var r = _hid[i];
                if (r != null) r.enabled = true;
            }
            _hid.Clear();
            for (int i = 0; i < _hidGo.Count; i++) { var g = _hidGo[i]; if (g != null) g.SetActive(true); }
            _hidGo.Clear();
            if (_scaled && _cur != null)
            {
                var root = RootOf(_cur);
                if (root != null) root.localScale = _origScale;
            }
            _scaled = false;
        }

        public static void Reset()
        {
            RestoreR();
            _cur = null;
        }
    }


    // ---------- 散射 / 速射（v1.4）----------
    public static class Shot
    {
        public static bool Scatter = true;      // 粉笔散射（霰弹枪）
        public static int Pellets = 7;          // 一次打出去几颗（含原来那颗）
        public static float SpreadDeg = 7f;     // 散布半角
        public static bool Rapid = true;        // 无视技能冷却
        public static bool AutoFire = true;     // 按住左键自动连发
        public static float FireRate = 0.05f;   // 连发间隔（秒）
        static float _lastFire;
        public static bool InfEnergy = true;    // 无限攻击体力
        public static string Last = "";

        static FieldInfo _fTarget, _fSpeed, _fDura, _fMeleeCd, _fDebugNo, _fSkillNo;
        static float _lastTick;
        static int _diag;

        static void Init()
        {
            if (_fTarget != null) return;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _fTarget = typeof(SkillComponent).GetField("targetDirect", BF);
            _fSpeed = typeof(SkillComponent).GetField("shootSpeed", BF);
            _fDura = typeof(SkillComponent).GetField("effectDura", BF);
            _fMeleeCd = typeof(TeacherMeleeComponent).GetField("attackCooldownRemaining", BF);
            _fDebugNo = typeof(TeacherMeleeComponent).GetField("debugNoCooldown", BF);
            _fSkillNo = typeof(SkillComponent).GetField("debugRightSkillNoCooldown", BF);
        }

        static Unit LocalUnit()
        {
            try
            {
                var um = UnitManager.Instance;
                if (um == null) return null;
                return um.localPlayer;
            }
            catch { return null; }
        }

        // 每帧把各种冷却压到 0（速射）
        public static void Tick()
        {
            float now0 = Time.unscaledTime;
            // 自动连发：按住鼠标左键就一直丢粉笔
            if (AutoFire && Input.GetMouseButton(0) && now0 - _lastFire >= FireRate)
            {
                _lastFire = now0;
                try
                {
                    Init();
                    var me0 = LocalUnit();
                    if (me0 != null)
                    {
                        var sk0 = me0.GetComponent<SkillComponent>();
                        if (sk0 != null) sk0.DealChalkEffect();
                    }
                }
                catch { }
            }
            if (!Rapid) return;
            float now = now0;
            if (now - _lastTick < 0.05f) return;
            _lastTick = now;
            try
            {
                Init();
                var me = LocalUnit();
                if (me == null) return;
                var sk = me.GetComponent<SkillComponent>();
                if (sk != null)
                {
                    sk.skillCoolDown = 0f;
                    if (_fSkillNo != null) _fSkillNo.SetValue(sk, true);
                }
                var mc = me.GetComponent<TeacherMeleeComponent>();
                if (mc != null)
                {
                    if (_fMeleeCd != null) _fMeleeCd.SetValue(mc, 0f);
                    if (_fDebugNo != null) _fDebugNo.SetValue(mc, true);
                }
            }
            catch { }
        }

        // 锥形随机散布
        static Vector3 Spread(Vector3 dir, float deg)
        {
            Vector3 right = Vector3.Cross(dir, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(dir, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, dir).normalized;
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float rad = Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f)) * (deg * Mathf.Deg2Rad);
            Vector3 off = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
            return (dir + off * Mathf.Tan(rad)).normalized;
        }

        // 粉笔：原来的那颗照飞，再补 Pellets-1 颗带散布的
        public static void Post_ShootChalk(SkillComponent __instance, int __0, float __1, float __2, bool __result)
        {
            if (_diag < 5) { _diag++; HackPlugin.Log("chalk hook scatter=" + Scatter + " result=" + __result + " id=" + __0 + " pellets=" + Pellets); }
            if (!Scatter || !__result) return;
            if (Pellets <= 1) return;
            Init();
            try
            {
                if (__instance == null || __0 != 4002) return;
                if (_fTarget == null) return;

                Vector3 baseDir = (Vector3)_fTarget.GetValue(__instance);
                if (baseDir.sqrMagnitude < 0.01f) return;

                var own = __instance.owner;
                if (own == null) return;
                ulong creater = own.UID;

                Camera cam = null;
                try { cam = SceneManager.Instance.MainCamera; } catch { }
                Vector3 spawn = cam != null ? cam.transform.position : own.Position + Vector3.up * 1.5f;

                var um = UnitManager.Instance;
                if (um == null) return;

                int n = Pellets - 1;
                if (n > 24) n = 24;

                for (int i = 0; i < n; i++)
                {
                    Vector3 dir = Spread(baseDir, SpreadDeg);
                    ulong c = creater;
                    float s = __1;
                    float d = __2;
                    UnitManager.RefAction act = delegate(ref UnitDataNet arg)
                    {
                        arg.CreaterUID = c;
                        arg.ShootForward = dir;
                        arg.ShootSpeed = s;
                        arg.EffectDuration = d;
                        arg.IgnoreSync = true;
                    };
                    um.CreateCommonUnitDirectly(4002, spawn, Vector3.zero, act, true);
                }
                Last = "x" + Pellets;
            }
            catch { }
        }

        // 无限攻击体力（只对本地自己生效）
        public static bool Pre_TryUseAttackEnergy(SkillComponent __instance, ref bool __result)
        {
            if (!Energy.On) return true;
            try
            {
                var o = __instance.owner;
                if (o == null || !o.isLocalPlayer) return true;   // 别人照常扣
            }
            catch { return true; }
            __result = true;
            return false;
        }

        // 无视冷却：进 DealSkillEffect 前先把冷却清掉
        public static void Pre_DealSkillEffect(SkillComponent __instance)
        {
            if (!Rapid) return;
            try { __instance.skillCoolDown = 0f; } catch { }
        }
    }

    // ---------- 模型染色（学生绿 / 老师蓝）----------
    public static class Skin
    {
        public static bool On = false;   // v5.1: 默认不上色，改用骨骼透视
        public static bool Wall = true;   // v4.7: 默认开 -> 模型透墙常驻最前: 默认关 -> 模型正常深度测试，隔墙不绘制
        public static Color Student = new Color(0.10f, 1.00f, 0.30f, 1f);   // 绿
        public static Color Teacher = new Color(1.00f, 0.15f, 0.15f, 1f);   // 红
        public static Color Npc = new Color(0.80f, 0.80f, 0.80f, 1f);
        public static string Last = "";

        public static Color ColorOf(Unit u)
        {
            int sub = 0;
            try { sub = (int)u.UnitSubType; } catch { }
            if (sub == 101) return Teacher;
            if (sub == 201) return Student;
            return Npc;
        }

        // 视线是否被墙挡住
        public static bool Blocked(Camera cam, Unit u, Vector3 p)
        {
            if (cam == null) return false;
            try
            {
                Vector3 from = cam.transform.position;
                Vector3 to = p + Vector3.up * 1.1f;
                Vector3 d = to - from;
                float len = d.magnitude;
                if (len < 1.5f) return false;
                RaycastHit h;
                if (!Physics.Raycast(from, d / len, out h, len - 0.8f,
                                     ~0, QueryTriggerInteraction.Ignore)) return false;
                Transform t = h.transform;
                if (t == null) return false;
                Transform root = null;
                try { root = u.model != null ? u.model.transform : u.transform; } catch { }
                if (root == null) return false;
                if (t == root || t.IsChildOf(root)) return false;
                return true;
            }
            catch { return false; }
        }


        static float _last;
        static bool _init;
        static int _cBase, _cMain, _cTint;
        static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        static readonly Dictionary<ulong, Renderer[]> _cache = new Dictionary<ulong, Renderer[]>();
        static readonly Dictionary<ulong, float> _cachedAt = new Dictionary<ulong, float>();

        static void Init()
        {
            if (_init) return;
            _init = true;
            _cBase = Shader.PropertyToID("_BaseColor");
            _cMain = Shader.PropertyToID("_Color");
            _cTint = Shader.PropertyToID("_TintColor");
        }

        static Renderer[] Renders(Unit u)
        {
            float now = Time.unscaledTime;
            float at;
            if (_cachedAt.TryGetValue(u.UID, out at) && now - at < 5f)
            {
                Renderer[] c;
                if (_cache.TryGetValue(u.UID, out c) && c != null) return c;
            }
            Transform root = null;
            try { root = u.model != null ? u.model.transform : u.transform; } catch { }
            Renderer[] rs = null;
            if (root != null)
            {
                try { rs = root.GetComponentsInChildren<Renderer>(true); } catch { }
            }
            if (rs == null) rs = new Renderer[0];
            _cache[u.UID] = rs;
            _cachedAt[u.UID] = now;
            return rs;
        }

        static bool _logged;
        static readonly Dictionary<Renderer, Color> _orig = new Dictionary<Renderer, Color>();

        // ---- v4.2：透视材质（Unlit + ZTest Always）。换掉 renderer 的材质，
        //      模型变成纯色剪影并且永远画在最前面 = 真·透墙看到人形 ----
        static Shader _wsh;
        static Material _mS, _mT, _mN;
        static readonly Dictionary<Renderer, Material> _origShared = new Dictionary<Renderer, Material>();
        static bool _lastWall = false;

        public static void SyncWallMode()
        {
            if (_lastWall == Wall) return;
            _lastWall = Wall;
            _mS = null; _mT = null; _mN = null;   // 重建材质（ZTest / renderQueue 不同）
        }

        static Material WallMat(int kind)
        {
            if (kind == 0 && _mS != null) return _mS;
            if (kind == 1 && _mT != null) return _mT;
            if (kind == 2 && _mN != null) return _mN;
            if (_wsh == null)
            {
                _wsh = Shader.Find("Universal Render Pipeline/Unlit");
                if (_wsh == null) _wsh = Shader.Find("Universal Render Pipeline/Lit");
                if (_wsh == null) _wsh = Shader.Find("Unlit/Color");
                if (_wsh == null) _wsh = Shader.Find("Sprites/Default");
            }
            if (_wsh == null) return null;
            Color c = kind == 0 ? new Color(Student.r, Student.g, Student.b, 1f)
                    : kind == 1 ? new Color(Teacher.r, Teacher.g, Teacher.b, 1f)
                                : new Color(Npc.r, Npc.g, Npc.b, 1f);
            Material m = null;
            try
            {
                m = new Material(_wsh);
                m.SetColor("_BaseColor", c);
                m.SetColor("_Color", c);
                m.SetColor("_EmissionColor", c * 0.35f);
                // v4.6：Wall 关 -> LEqual（正常深度，墙后自动不可见）；Wall 开 -> Always（穿透）
                m.SetInt("_ZTest", (int)(Wall ? UnityEngine.Rendering.CompareFunction.Always
                                             : UnityEngine.Rendering.CompareFunction.LessEqual));
                m.SetInt("_ZWrite", Wall ? 0 : 1);
                m.SetInt("_Surface", 0);          // opaque
                m.renderQueue = Wall ? 4000 : 2000;   // Geometry 队列 = 跟普通物体一起参与深度
            }
            catch { }
            if (kind == 0) _mS = m; else if (kind == 1) _mT = m; else _mN = m;
            return m;
        }

        static void PaintWall(Renderer r, int kind)
        {
            if (r == null) return;
            if (Wall)
            {
                Material m = WallMat(kind);
                if (m == null) { Paint(r, kind == 0 ? Student : (kind == 1 ? Teacher : Npc)); return; }
                if (!_origShared.ContainsKey(r)) { try { _origShared[r] = r.sharedMaterial; } catch { _origShared[r] = null; } }
                try { if (r.sharedMaterial != m) r.sharedMaterial = m; } catch { }
            }
            else
            {
                Material o;
                if (_origShared.TryGetValue(r, out o)) { try { r.sharedMaterial = o; } catch { } _origShared.Remove(r); }
                Paint(r, kind == 0 ? Student : (kind == 1 ? Teacher : Npc));
            }
        }

        static void Paint(Renderer r, Color c)
        {
            if (r == null) return;
            // 1) property block（不污染共享材质）
            try
            {
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(_cBase, c);
                _mpb.SetColor(_cMain, c);
                _mpb.SetColor(_cTint, c);
                r.SetPropertyBlock(_mpb);
            }
            catch { }
            // 2) 材质兜底（有些 shader 不吃 property block）
            try
            {
                var m = r.material;
                if (m != null)
                {
                    if (!_orig.ContainsKey(r))
                    {
                        Color o = Color.white;
                        try { o = m.GetColor(_cBase); } catch { }
                        _orig[r] = o;
                    }
                    try { m.color = c; } catch { }
                    try { m.SetColor(_cBase, c); } catch { }
                    try { m.SetColor(_cMain, c); } catch { }
                    if (!_logged)
                    {
                        _logged = true;
                        try { HackPlugin.Log("paint shader=" + m.shader.name + " mat=" + m.name + " prop-block+material"); }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (!On) return;
            if (now - _last < 0.25f) return;
            _last = now;
            Init();
            SyncWallMode();
            try
            {
                var um = UnitManager.Instance;
                if (um == null || um.unitsDict == null) return;
                int nS = 0, nT = 0;
                foreach (var kv in um.unitsDict)
                {
                    var u = kv.Value;
                    if (u == null) continue;
                    bool self = false;
                    try { self = u.isLocalPlayer; } catch { }
                    if (self) continue;              // v4.6：不给自己上色
                    EUnitSubType st;
                    try { st = u.UnitSubType; } catch { continue; }
                    Color c; int kind;
                    if (st == EUnitSubType.Teacher) { c = Teacher; kind = 1; nT++; }
                    else if (st == EUnitSubType.Student) { c = Student; kind = 0; nS++; }
                    else if (st == EUnitSubType.Student_NPC) { c = Npc; kind = 2; }
                    else continue;
                    var rs = Renders(u);
                    for (int i = 0; i < rs.Length; i++) PaintWall(rs[i], kind);
                }
                Last = "师" + nT + "/生" + nS;
            }
            catch { }
        }

        public static void Restore()
        {
            try
            {
                foreach (var kv in _orig)
                {
                    var r = kv.Key;
                    if (r == null) continue;
                    try { r.material.color = kv.Value; } catch { }
                    try { r.material.SetColor(_cBase, kv.Value); } catch { }
                    try { r.material.SetColor(_cMain, kv.Value); } catch { }
                    try { r.SetPropertyBlock(null); } catch { }
                }
            }
            catch { }
            try
            {
                foreach (var kv in _origShared)
                {
                    if (kv.Key == null) continue;
                    try { kv.Key.sharedMaterial = kv.Value; } catch { }
                }
            }
            catch { }
            _origShared.Clear();
            _orig.Clear();
            _cache.Clear();
            _cachedAt.Clear();
            Last = "";
        }
    }

    // ---------- harmony side ----------
    // ============================================================
    //  v7.0  UiKit —— 高级悬浮菜单（玻璃拟态 / 圆角 / 动画 / 可点击）
    // ============================================================
    //  v7.0  UiKit —— 高级悬浮菜单（玻璃拟态 / 圆角 / 动画 / 可点击）
    // ============================================================
    //  v8.0  液态玻璃 UI（Liquid Glass）—— 对应 ui/liquid-glass.html
    //  Glass  : 低频抓屏 → 硬件降采样 → box blur，做真·背景模糊
    //  Blobs  : 全屏弥散光斑（深紫/电光蓝/青/品红），缓慢漂移
    //  Spring : cubic-bezier(.34,1.56,.64,1) 的等价弹簧，带过冲
    // ============================================================

    public class Spring
    {
        public float Value, Target, Vel;
        public Spring(float v) { Value = v; Target = v; Vel = 0f; }
        public void To(float t) { Target = t; }
        public void Step(float dt)
        {
            if (dt > 0.05f) dt = 0.05f;
            float a = (Target - Value) * 88f - Vel * 13f;   // v8.3: 慢速收敛（约 0.55s），轻微回弹
            Vel += a * dt;
            Value += Vel * dt;
            if (Mathf.Abs(Target - Value) < 0.0008f && Mathf.Abs(Vel) < 0.0008f) { Value = Target; Vel = 0f; }
        }
    }

    public static class Glass
    {
        public static bool On = false;   // v8.5: 改回纯深色面板，不再抓游戏画面当背景
        public static float Interval = 0.22f;

        static Texture2D _blur;
        static int _bw, _bh;
        static float _next;
        static int _fails;

        public static Texture2D Tex { get { return _blur; } }

        public static void Tick()
        {
            if (!On) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            try { Grab(); }
            catch
            {
                _fails++;
                if (_fails >= 3) { On = false; HackPlugin.Log("[WceHack] 抓屏失败，玻璃退回纯色"); }
            }
        }

        static void Grab()
        {
            Texture2D src = ScreenCapture.CaptureScreenshotAsTexture();
            if (src == null) { _fails++; return; }

            int sw = Mathf.Max(64, Screen.width / 16);
            int sh = Mathf.Max(36, Screen.height / 16);
            if (_blur == null || _bw != sw || _bh != sh)
            {
                if (_blur != null) UnityEngine.Object.Destroy(_blur);
                _blur = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
                _blur.hideFlags = HideFlags.HideAndDontSave;
                _blur.filterMode = FilterMode.Bilinear;
                _blur.wrapMode = TextureWrapMode.Clamp;
                _bw = sw; _bh = sh;
            }

            RenderTexture rt = RenderTexture.GetTemporary(sw, sh, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                _blur.ReadPixels(new Rect(0, 0, sw, sh), 0, 0, false);
                _blur.Apply(false);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(src);
            }

            Color[] px = _blur.GetPixels();
            px = Box(px, sw, sh, 2);
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                c.r = Mathf.Lerp(l, c.r, 1.8f) * 1.15f;
                c.g = Mathf.Lerp(l, c.g, 1.8f) * 1.15f;
                c.b = Mathf.Lerp(l, c.b, 1.8f) * 1.15f;
                c.a = 1f;
                px[i] = c;
            }
            _blur.SetPixels(px);
            _blur.Apply(false);
            _fails = 0;
        }

        static Color[] Box(Color[] src, int w, int h, int pass)
        {
            Color[] a = src;
            Color[] b = new Color[src.Length];
            for (int p = 0; p < pass; p++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color s = new Color(0, 0, 0, 0);
                        int n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int yy = y + dy; if (yy < 0 || yy >= h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx; if (xx < 0 || xx >= w) continue;
                                s += a[yy * w + xx]; n++;
                            }
                        }
                        b[y * w + x] = s / Mathf.Max(1, n);
                    }
                }
                Color[] t = a; a = b; b = t;
            }
            return a;
        }
    }

    public static class Blobs
    {
        public static bool On = false;   // v8.5: 光斑会让底色跟画面一起变，关掉更稳
        static Texture2D _rad;
        static bool _built;

        static readonly Color[] Col = new Color[]
        {
            new Color(0.545f, 0.361f, 0.965f, 1f),   // 深紫
            new Color(0.231f, 0.510f, 0.965f, 1f),   // 电光蓝
            new Color(0.024f, 0.714f, 0.831f, 1f),   // 青
            new Color(0.910f, 0.475f, 0.976f, 1f),   // 品红
            new Color(0.220f, 0.741f, 0.973f, 1f)    // 亮蓝
        };

        static void Build()
        {
            if (_built) return;
            _built = true;
            int n = 128;
            _rad = new Texture2D(n, n, TextureFormat.ARGB32, false);
            _rad.hideFlags = HideFlags.HideAndDontSave;
            _rad.filterMode = FilterMode.Bilinear;
            _rad.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * a;
                    _rad.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            _rad.Apply();
        }

        public static void Draw(Rect area)
        {
            if (!On) return;
            Build();
            float t = Time.unscaledTime;
            float S = Mathf.Max(area.width, area.height);

            for (int i = 0; i < Col.Length; i++)
            {
                float ph = i * 1.618f;
                float dx = Mathf.Sin(t * 0.11f + ph) * 0.16f + Mathf.Sin(t * 0.043f + ph * 2f) * 0.08f;
                float dy = Mathf.Cos(t * 0.09f + ph * 1.3f) * 0.14f + Mathf.Cos(t * 0.037f + ph) * 0.07f;
                float sz = S * (0.72f + 0.12f * Mathf.Sin(t * 0.07f + ph));
                float cx = area.x + area.width * (0.12f + 0.19f * i) + dx * S;
                float cy = area.y + area.height * (0.3f + 0.12f * Mathf.Sin(ph)) + dy * S;

                Color c = Col[i];
                c.a = 0.42f;
                GUI.color = c;
                GUI.DrawTexture(new Rect(cx - sz * 0.5f, cy - sz * 0.5f, sz, sz), _rad);
            }
            GUI.color = Color.white;
        }
    }

    public static class UiKit
    {
        // ---- 配色（对齐 liquid-glass.html）----
        public static Color CMint = new Color(0.373f, 0.941f, 0.784f, 1f);
        public static Color CCyan = new Color(0.133f, 0.827f, 0.933f, 1f);
        public static Color CInk = new Color(0.039f, 0.039f, 0.047f, 1f);   // v9.2: 更黑，白卡上不发虚
        public static Color CInk2 = new Color(0.353f, 0.353f, 0.376f, 1f);  // iOS tertiaryLabel
        public static Color CInk3 = new Color(0.600f, 0.600f, 0.620f, 1f);  // v9.2: 次要文字

        public static bool Open = true;      // v8.3: 进对局默认展开一次
        public static int Cat = 0;

        // v8.3：只保留一个可自定义的菜单键，其余热键全部取消
        public static KeyCode MenuKey = KeyCode.F2;
        public static bool Binding = false;
        static float _bindAt = -99f;

        public static string KeyName()
        {
            KeyCode k = MenuKey;
            if (k == KeyCode.Insert) return "Ins";
            if (k == KeyCode.PageUp) return "PgUp";
            if (k == KeyCode.PageDown) return "PgDn";
            if (k == KeyCode.Delete) return "Del";
            if (k == KeyCode.BackQuote) return "`";
            if (k == KeyCode.LeftBracket) return "[";
            if (k == KeyCode.RightBracket) return "]";
            if (k == KeyCode.Backslash) return "\\";
            if (k == KeyCode.Return) return "Enter";
            if (k == KeyCode.Escape) return "Esc";
            if (k == KeyCode.Space) return "Space";
            if (k == KeyCode.Tab) return "Tab";
            if (k == KeyCode.Home) return "Home";
            if (k == KeyCode.End) return "End";
            string s = k.ToString();
            if (s.StartsWith("Alpha")) return s.Substring(5);
            if (s.StartsWith("Keypad")) return "Num" + s.Substring(6);
            if (s.StartsWith("Left") || s.StartsWith("Right")) return s.StartsWith("Left") ? "L-" + s.Substring(4) : "R-" + s.Substring(5);
            return s.ToUpper();
        }

        const float W = 1220f, H = 1040f;
        const float R_OUT = 32f, R_CARD = 22f;

        // ---- 贴图 ----
        static Texture2D _texShadow, _texPanel, _texRim, _texCard, _texCardHi, _texCardOn,
                          _texCat, _texCatOn, _texPillOn, _texPillOff, _texKnob,
                          _texSpot, _texGrad, _texWhite;
        static GUIStyle _sShadow, _sPanel, _sRim, _sCard, _sCardHi, _sCardOn, _sCat, _sCatOn, _sPillOn, _sPillOff;

        static GUIStyle _stTitle, _stSub, _stRow, _stTip, _stKey, _stCatOn, _stCatOff, _stFoot, _stVal, _stClose;
        static Font _font;
        static bool _built;

        static Spring _anim = new Spring(0f);
        static Spring _hover = new Spring(0f);
        static float _openAt = -99f;
        static string _tip = "";
        static Rect _panelRect;
        static Vector2 _mouse;
        static bool _click, _drag;   // v8.2: IMGUI 原生鼠标事件（group 局部坐标）
        static readonly System.Collections.Generic.Dictionary<string, Spring> _an =
            new System.Collections.Generic.Dictionary<string, Spring>();

        /// <summary>v8.3：每个开关一条弹簧，来回切换都有慢速过渡</summary>
        static float A(string key, bool on)
        {
            Spring sp;
            if (!_an.TryGetValue(key, out sp)) { sp = new Spring(on ? 1f : 0f); _an[key] = sp; }
            sp.Target = on ? 1f : 0f;
            sp.Step(Time.unscaledDeltaTime);
            return Mathf.Clamp01(sp.Value);
        }

        public static readonly string[] Cats = new string[] { "视觉", "移动", "战斗", "互动", "防护" };

        // ---------- 圆角贴图（上下不同描边色 = 物理 Rim Light）----------
        public static Texture2D Round(int size, int r, Color fill, Color rimTop, Color rimBottom, float bw)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.ARGB32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < size; y++)
            {
                float ty = (float)y / (size - 1);           // 0 底 -> 1 顶
                Color rim = Color.Lerp(rimBottom, rimTop, ty);
                for (int x = 0; x < size; x++)
                {
                    float dx = 0f, dy = 0f;
                    if (x < r) dx = r - x; else if (x >= size - r) dx = x - (size - r - 1);
                    if (y < r) dy = r - y; else if (y >= size - r) dy = y - (size - r - 1);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c;
                    if (d > r) c = new Color(0f, 0f, 0f, 0f);
                    else if (d > r - bw) c = rim;
                    else c = fill;
                    t.SetPixel(x, y, c);
                }
            }
            t.Apply();
            return t;
        }

        static GUIStyle St(Texture2D tex, int border)
        {
            GUIStyle s = new GUIStyle();
            s.normal.background = tex;
            s.border = new RectOffset(border, border, border, border);
            s.padding = new RectOffset(0, 0, 0, 0);
            s.margin = new RectOffset(0, 0, 0, 0);
            return s;
        }

        public static Font F()
        {
            if (_font != null) return _font;
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "Segoe UI", "Arial" }, 18);
            }
            catch { }
            return _font;
        }

        static GUIStyle Mk(int size, Color col)
        {
            GUIStyle s = new GUIStyle();
            s.fontSize = size;
            s.normal.textColor = col;
            s.clipping = TextClipping.Clip;
            if (F() != null) s.font = F();
            return s;
        }

        static void Build()
        {
            if (_built) return;
            _built = true;

            Color ink = new Color(0.973f, 0.973f, 0.984f, 1f);      // v9.2: 更浅的分组底色，卡片几乎融在一起
            Color rimT = new Color(1f, 1f, 1f, 0.95f);
            Color rimB = new Color(0f, 0f, 0f, 0.10f);

            _texShadow = Round(64, 22, new Color(0f, 0f, 0f, 0.16f), new Color(0f, 0f, 0f, 0.16f), new Color(0f, 0f, 0f, 0.16f), 0f);
            _texPanel = Round(120, 56, ink, rimT, rimB, 1.6f);
            _texRim = Round(120, 56, new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 0.9f), new Color(0f, 0f, 0f, 0.10f), 1.4f);

            _texCard = Round(48, 22, new Color(1f, 1f, 1f, 1f), new Color(0f, 0f, 0f, 0.085f), new Color(0f, 0f, 0f, 0.075f), 1.3f);
            _texCardHi = Round(48, 22, new Color(1f, 1f, 1f, 1f), new Color(0f, 0.478f, 1f, 0.30f), new Color(0f, 0.478f, 1f, 0.22f), 1.4f);
            _texCardOn = Round(48, 22, new Color(0f, 0.478f, 1f, 0.10f), new Color(0f, 0.478f, 1f, 0.42f), new Color(0f, 0.478f, 1f, 0.28f), 1.4f);
            _texCat = Round(48, 22, new Color(0f, 0f, 0f, 0.07f), new Color(0f, 0f, 0f, 0.09f), new Color(0f, 0f, 0f, 0.04f), 1f);
            _texCatOn = Round(48, 22, new Color(0f, 0.478f, 1f, 1f), new Color(1f, 1f, 1f, 0.55f), new Color(0f, 0.36f, 0.80f, 1f), 1.2f);
            _texPillOn = Round(32, 16, new Color(0.204f, 0.780f, 0.349f, 1f), new Color(1f, 1f, 1f, 0.60f), new Color(0.13f, 0.60f, 0.24f, 1f), 1.2f);
            _texPillOff = Round(32, 16, new Color(0f, 0f, 0f, 0.10f), new Color(0f, 0f, 0f, 0.06f), new Color(0f, 0f, 0f, 0.05f), 1f);
            _texKnob = Round(32, 16, new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f), new Color(0.86f, 0.87f, 0.89f, 1f), 1f);

            // 指针光斑
            int n = 96;
            _texSpot = new Texture2D(n, n, TextureFormat.ARGB32, false);
            _texSpot.hideFlags = HideFlags.HideAndDontSave;
            _texSpot.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d * 1.4f);
                    _texSpot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            _texSpot.Apply();

            // 玻璃竖直渐变（v8.3 深色：顶部深墨蓝 62% → 底部 78%，不再用白色）
            int g = 64;
            _texGrad = new Texture2D(4, g, TextureFormat.ARGB32, false);
            _texGrad.hideFlags = HideFlags.HideAndDontSave;
            _texGrad.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < g; y++)
            {
                float t = (float)y / (g - 1);            // 0 底 -> 1 顶
                float a = t > 0.5f ? Mathf.Lerp(0.0f, 0.02f, (t - 0.5f) * 2f) : 0f;
                for (int x = 0; x < 4; x++) _texGrad.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            _texGrad.Apply();

            _texWhite = Texture2D.whiteTexture;

            _sShadow = St(_texShadow, 22);
            _sPanel = St(_texPanel, 56);
            _sRim = St(_texRim, 56);
            _sCard = St(_texCard, 22);
            _sCardHi = St(_texCardHi, 22);
            _sCardOn = St(_texCardOn, 22);
            _sCat = St(_texCat, 22);
            _sCatOn = St(_texCatOn, 22);
            _sPillOn = St(_texPillOn, 16);
            _sPillOff = St(_texPillOff, 16);

            _stTitle = Mk(26, new Color(0f, 0f, 0f, 1f)); _stTitle.fontStyle = FontStyle.Bold;
            _stSub = Mk(13, CInk3);
            _stRow = Mk(17, CInk); _stRow.fontStyle = FontStyle.Bold;
            _stTip = Mk(12, CInk3);
            _stKey = Mk(13, CInk2); _stKey.alignment = TextAnchor.MiddleCenter; _stKey.fontStyle = FontStyle.Bold;
            _stCatOn = Mk(14, new Color(1f, 1f, 1f, 1f)); _stCatOn.fontStyle = FontStyle.Bold; _stCatOn.alignment = TextAnchor.MiddleCenter;
            _stCatOff = Mk(14, CInk2); _stCatOff.alignment = TextAnchor.MiddleCenter;
            _stFoot = Mk(12, CInk2);
            _stVal = Mk(13, CMint); _stVal.fontStyle = FontStyle.Bold; _stVal.alignment = TextAnchor.MiddleRight;
            _stClose = Mk(16, CInk2); _stClose.alignment = TextAnchor.MiddleCenter;
        }

        // ---------- 基础 ----------
        static void Box(Rect r, GUIStyle s) { GUI.Box(r, GUIContent.none, s); }

        static void Fill(Rect r, Color c)
        {
            Color o = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, _texWhite);
            GUI.color = o;
        }

        static void Tex(Rect r, Texture2D t, Color c)
        {
            Color o = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true);
            GUI.color = o;
        }

        static Vector2 Mp() { return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y); }

        // ---------- 行 ----------
        static void RowToggle(Rect r, string name, string key, bool on, string tip)
        {
            bool hov = r.Contains(_mouse);
            float v = A(name, on);                       // v8.3: 0..1 弹簧插值
            Box(r, hov ? _sCardHi : _sCard);
            if (v > 0.002f)
            {
                Color oc0 = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, v);
                Box(r, _sCardOn);
                GUI.color = oc0;
            }

            GUI.Label(new Rect(r.x + 22f, r.y + 6f, r.width - 190f, 24f), name, _stRow);
            GUI.Label(new Rect(r.x + 22f, r.y + 30f, r.width - 190f, 17f), tip, _stTip);

            if (key != null && key.Length > 0)
            {
                float kw = 12f + key.Length * 7f;
                Rect kr = new Rect(r.x + r.width - 96f - kw, r.y + (r.height - 22f) * 0.5f, kw, 22f);
                Box(kr, _sCat);
                GUI.Label(kr, key, _stKey);
            }

            float pw = 58f, ph = 32f;
            Rect pill = new Rect(r.x + r.width - 62f, r.y + (r.height - ph) * 0.5f, pw, ph);
            Box(pill, _sPillOff);
            if (v > 0.002f)
            {
                Color oc1 = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, v);
                Box(pill, _sPillOn);
                GUI.color = oc1;
            }
            float kx = Mathf.Lerp(pill.x + 3f, pill.x + pw - 27f, v);
            Tex(new Rect(kx, pill.y + 3f, 24f, 24f), _texKnob,
                Color.Lerp(Color.white, new Color(0.05f, 0.20f, 0.17f, 1f), v));

            if (hov)
            {
                _tip = tip;
                if (_click) ClickToggle(name);
            }
        }

        static void RowButton(Rect r, string name, string tip, string key)
        {
            bool hov = r.Contains(_mouse);
            Box(r, hov ? _sCardHi : _sCard);
            if (hov) Fill(new Rect(r.x + 14f, r.y + 1f, r.width - 28f, 1f), new Color(1f, 1f, 1f, 0.18f));
            GUI.Label(new Rect(r.x + 20f, r.y + 5f, r.width - 180f, 19f), name, _stRow);
            if (tip != null && tip.Length > 0)
                GUI.Label(new Rect(r.x + 20f, r.y + 24f, r.width - 180f, 15f), tip, _stTip);
            if (key != null && key.Length > 0)
            {
                float kw = 12f + key.Length * 7f;
                Rect kr = new Rect(r.x + r.width - 18f - kw, r.y + (r.height - 22f) * 0.5f, kw, 22f);
                Box(kr, _sCat);
                GUI.Label(kr, key, _stKey);
            }
            if (hov) { _tip = tip; if (_click) ClickButton(name); }
        }

        static void RowSlider(Rect r, string name, float val, float min, float max, string unit)
        {
            Box(r, _sCard);
            GUI.Label(new Rect(r.x + 18f, r.y + 9f, 260f, 20f), name, _stRow);
            string txt = (Mathf.Abs(val % 1f) < 0.001f ? val.ToString("0") : val.ToString("0.0")) + unit;
            GUI.Label(new Rect(r.x + r.width - 130f, r.y + 9f, 112f, 20f), txt, _stVal);

            Rect bar = new Rect(r.x + 18f, r.y + 40f, r.width - 36f, 6f);
            Fill(bar, new Color(1f, 1f, 1f, 0.10f));
            float t = Mathf.Clamp01((val - min) / Mathf.Max(0.001f, max - min));
            Fill(new Rect(bar.x, bar.y, bar.width * t, bar.height), new Color(0.29f, 0.88f, 0.78f, 0.95f));
            Fill(new Rect(bar.x, bar.y, bar.width * t, 2f), new Color(1f, 1f, 1f, 0.22f));
            Tex(new Rect(bar.x + bar.width * t - 8f, bar.y - 5f, 16f, 16f), _texKnob, Color.white);

            Rect hit = new Rect(bar.x - 6f, bar.y - 12f, bar.width + 12f, 30f);
            if (hit.Contains(_mouse) && (_click || _drag))
                ClickSlider(name, Mathf.Lerp(min, max, Mathf.Clamp01((_mouse.x - bar.x) / bar.width)));
        }

        // ---------- 交互 ----------
        public static void ClickToggle(string name)
        {
            if (name == "透视 ESP") { HackBehaviour.Esp = !HackBehaviour.Esp; }
            else if (name == "骨骼透视") { HackBehaviour.Bones = !HackBehaviour.Bones; }
            else if (name == "模型染色") { Skin.On = !Skin.On; if (!Skin.On) Skin.Restore(); }
            else if (name == "染色透墙") { Skin.Wall = !Skin.Wall; Skin.SyncWallMode(); }
            else if (name == "显示 NPC") { HackBehaviour.ShowNpc = !HackBehaviour.ShowNpc; Aim.NpcTarget = HackBehaviour.ShowNpc; }
            else if (name == "加速 x2.5") { HackPatches.Speed = !HackPatches.Speed; }
            else if (name == "无限冲刺") { HackPatches.InfSprint = !HackPatches.InfSprint; }
            else if (name == "御剑飞行") { Fly.On = !Fly.On; }
            else if (name == "第三人称") { ThirdPerson.On = !ThirdPerson.On; }
            else if (name == "自动旋转") { AutoSpin.On = !AutoSpin.On; if (!AutoSpin.On) AutoSpin.Reset(); }
            else if (name == "无限体力") { Energy.On = !Energy.On; }
            else if (name == "散射（霰弹）") { Shot.Scatter = !Shot.Scatter; }
            else if (name == "速射 + 连发") { Shot.Rapid = !Shot.Rapid; Shot.AutoFire = Shot.Rapid; }
            else if (name == "攻击穿墙") { WallHack.On = !WallHack.On; }
            else if (name == "秒完成互动") { Instant.On = !Instant.On; }
            else if (name == "无限物品") { Instant.InfItems = !Instant.InfItems; }
            else if (name == "竹筒无限用") { Taser.On = !Taser.On; }
            else if (name == "显示名字") { NameTag.On = !NameTag.On; }
            else if (name == "准星锁定") { Selector.On = !Selector.On; }
            else if (name == "刷闪光弹") { Flash.On = !Flash.On; }
            else if (name == "击倒老师") { Knock.On = !Knock.On; if (Knock.On) Knock.Do(); }
            else if (name == "自动放倒") { Slip.On = !Slip.On; }
            else if (name == "缩小全场") { Mini.On = !Mini.On; }
            else if (name == "栽赃陷害") { Frame.On = !Frame.On; if (Frame.On) Frame.PickNext(); }
            else if (name == "抽屉塞竹筒") { DrawerHook.On = !DrawerHook.On; if (DrawerHook.On) DrawerHook.FillAll(); }
            else if (name == "无限投掷") { Stock.On = !Stock.On; if (Stock.On) Stock.ClearCooldown(); }
            else if (name == "不可被抓") { Protect.NoCatch = !Protect.NoCatch; }
            else if (name == "不可被打晕") { Protect.NoStun = !Protect.NoStun; }
            else if (name == "完全隐形") { Ghost.On = !Ghost.On; if (!Ghost.On) Ghost.Reset(); }
            else if (name == "自动刷新状态") { Refresh.On = !Refresh.On; }
            else if (name == "视角锁定") { StableCam.On = !StableCam.On; }
            else if (name == "防飞出地图") { Safe.On = !Safe.On; }
        }

        public static void ClickButton(string name)
        {
            if (name == "秒解救同伴") Rescue.Do();
            else if (name == "立即脱困") HackBehaviour.DoPanic();
            else if (name == "传送到老师") HackBehaviour.Teleport(true);
            else if (name == "传送到学生") HackBehaviour.Teleport(false);
            else if (name == "刷新状态一次") Refresh.Do();
            else if (name == "放倒老师") Slip.Do();
            else if (name == "抽屉塞竹筒") DrawerHook.FillAll();
        }

        static void ClickSlider(string name, float v)
        {
            if (name == "距离上限") HackBehaviour.MaxDist = Mathf.Round(v / 10f) * 10f;
            else if (name == "第三人称距离") ThirdPerson.Distance = v;
            else if (name == "旋转速度") AutoSpin.Speed = v;
        }

        // ---------- 主绘制 ----------
        public static void Draw()
        {
            if (!Open) return;
            Build();

            _anim.To(Open ? 1f : 0f);
            _anim.Step(Time.unscaledDeltaTime);
            float e = Mathf.Clamp01(_anim.Value);
            if (e <= 0.001f) return;

            _mouse = Mp();

            // 改键：捕获任意按键
            if (Binding)
            {
                if (Time.unscaledTime - _bindAt > 0.25f && Event.current != null &&
                    Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
                {
                    MenuKey = Event.current.keyCode;
                    Binding = false;
                    HackPlugin.Log("[WceHack] 菜单键改为 " + MenuKey);
                    Event.current.Use();
                }
            }

            float X = (Screen.width - W) * 0.5f;
            float Y = (Screen.height - H) * 0.5f + (1f - e) * 26f;
            float scale = 0.965f + 0.035f * e;
            _panelRect = new Rect(
                X + W * (1f - scale) * 0.5f,
                Y + H * (1f - scale) * 0.5f,
                W * scale, H * scale);

            Rect p = _panelRect;
            Color old = GUI.color;
            // v9.2: 不再把打开动画的 e 乘进 GUI.color —— 那会让整块 UI（尤其文字）半透明发虚
            GUI.color = Color.white;

            // 1) 全屏遮罩
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.30f * e));

            // 2) 外投影（放大 18px，下移 10px）
            Box(new Rect(p.x - 14f, p.y - 6f, p.width + 28f, p.height + 32f), _sShadow);

            // 3) 玻璃内部：裁剪到面板
            GUI.BeginGroup(new Rect(p.x, p.y, p.width, p.height));
            // v8.2: 控件 Rect 都是 group 局部坐标，鼠标必须用 Event.current（自动局部化）
            if (Event.current != null)
            {
                _mouse = Event.current.mousePosition;
                _click = (Event.current.type == EventType.MouseDown && Event.current.button == 0);
                _drag = (Event.current.type == EventType.MouseDrag);
            }
            else
            {
                _mouse = new Vector2(Mp().x - p.x, Mp().y - p.y);
                _click = Input.GetMouseButtonDown(0);
                _drag = Input.GetMouseButton(0);
            }

            Rect local = new Rect(0, 0, p.width, p.height);

            // 3a0) 面板底色（v8.5: 之前底色全靠抓屏层和渐变，关掉玻璃后就变透明了 —— 这里直接铺深色圆角）
            Box(local, _sPanel);

            // 3a) 抓屏模糊层（只取面板对应区域）
            Texture2D bt = Glass.On ? Glass.Tex : null;
            if (bt != null && Glass.On)
            {
                float ux = p.x / Mathf.Max(1, Screen.width);
                float uy = 1f - (p.y + p.height) / Mathf.Max(1, Screen.height);
                float uw = p.width / Mathf.Max(1, Screen.width);
                float uh = p.height / Mathf.Max(1, Screen.height);
                // v8.3: 深色玻璃 —— 模糊画面压暗、去饱和，只留一点轮廓
                Color bc = new Color(0.30f, 0.34f, 0.46f, 0.58f);
                Color o2 = GUI.color; GUI.color = bc;
                GUI.DrawTextureWithTexCoords(local, bt, new Rect(ux, uy, uw, uh));
                GUI.color = o2;
            }

            // 3b) 弥散光斑（面板内）
            if (Blobs.On) Blobs.Draw(new Rect(-p.width * 0.15f, -p.height * 0.2f, p.width * 1.3f, p.height * 1.4f));

            // 3c) 玻璃渐变叠色（v8.5: 改成很淡的顶部高光，不再压底色）
            Tex(local, _texGrad, Color.white);

            // 3d) 指针跟随光斑
            if (new Rect(0f, 0f, p.width, p.height).Contains(_mouse))
            {
                float sr = 320f;
                Tex(new Rect(_mouse.x - sr * 0.5f, _mouse.y - sr * 0.5f, sr, sr),
                    _texSpot, new Color(1f, 1f, 1f, 0.13f));
            }

            // 3e) 内部内容
            DrawContent(p, e);

            GUI.EndGroup();

            // 4) 物理 Rim Light 描边（不裁剪，压在内部之上）
            Box(new Rect(p.x, p.y, p.width, p.height), _sRim);

            GUI.color = old;
        }

        public class TRow
        {
            public string name, key, tip;
            public int kind;                 // 0=开关 1=按钮
            public TRow(string n, string k, string t, int kd) { name = n; key = k; tip = t; kind = kd; }
        }

        public static readonly TRow[] T = new TRow[]
        {
            new TRow("透视 ESP", "F1", "画出老师 / 学生 / NPC 的位置", 0),
            new TRow("骨骼透视", "F12", "只画躯干 + 两臂 + 两腿", 0),
            new TRow("模型染色", "F11", "学生绿 / 老师红，整模型换色", 0),
            new TRow("染色透墙", "End", "被挡住也完整绘制", 0),
            new TRow("显示 NPC", "F10", "把 NPC 学生也画出来", 0),
            new TRow("显示名字", "F9", "头顶玩家名字开关", 0),
            new TRow("加速 x2.5", "F8", "本地移动速度 x2.5", 0),
            new TRow("无限冲刺", "F7", "冲刺不耗体力", 0),
            new TRow("御剑飞行", "PgUp", "WASD 平移 · 空格升 · Ctrl 降", 0),
            new TRow("第三人称", "H", "把相机往后拉，看自己的背影", 0),
            new TRow("自动旋转", "B", "自己原地匀速转圈", 0),
            new TRow("无限体力", "Home", "技能能量 9999 / 疲劳归零", 0),
            new TRow("散射（霰弹）", "F3", "一颗粉笔炸成 7 颗", 0),
            new TRow("速射 + 连发", "F4", "无冷却 + 按住左键 20 发/秒", 0),
            new TRow("攻击穿墙", "1", "粉笔/黑板擦/剑气无视墙体", 0),
            new TRow("准星锁定", "7", "屏幕中央圆圈自动锁定目标", 0),
            new TRow("竹筒无限用", "T", "电击枪/竹筒不消耗，反复吹", 0),
            new TRow("刷闪光弹", "6", "脚底下持续刷闪光弹", 0),
            new TRow("秒完成互动", "9", "写板书 / 吃东西 / 吹笛 / 取货", 0),
            new TRow("无限物品", "0", "手持物品耐久永远满", 0),
            new TRow("无限投掷", "PgDn", "烟雾 / 闪光扔完立刻补", 0),
            new TRow("不可被抓", "3", "老师抓不住你", 0),
            new TRow("不可被打晕", "4", "眩晕状态与特效都进不来", 0),
            new TRow("完全隐形", "Del", "模型 + 名字 + 剪影全部消失", 0),
            new TRow("自动刷新状态", "Y", "每 0.08 秒清掉被抓/眩晕/挂载", 0),
            new TRow("视角锁定", "5", "被晕了也不抖镜头", 0),
            new TRow("防飞出地图", "G", "掉出地图自动拽回地面", 0),
            new TRow("击倒老师", "8", "把老师按倒下（动画层）", 0),
            new TRow("放倒老师", "L", "走香蕉皮通道，老师本机真滑倒", 1),
            new TRow("自动放倒", "K", "每 2 秒自动放倒最近老师", 0),
            new TRow("缩小全场", "M", "所有人模型缩到 15%", 0),
            new TRow("栽赃陷害", "N", "让别人原地旋转 + 飞起来", 0),
            new TRow("抽屉塞竹筒", "O", "打开抽屉就塞一根竹筒", 0),
            new TRow("传送到老师", "F5", "瞬移到最近老师身边", 1),
            new TRow("传送到学生", "F6", "瞬移到最近学生身边", 1),
            new TRow("秒解救同伴", "2", "柱子上的和老师手上的全放下来", 1),
            new TRow("立即脱困", "Ins", "清掉挂载 / 眩晕 / 柱子绑定", 1),
            new TRow("刷新状态一次", "U", "手动跑一次状态清理", 1),
        };

        static void DrawContent(Rect p, float e)
        {
            // 标题
            Fill(new Rect(30f, 28f, 3.5f, 30f), new Color(0f, 0.478f, 1f, 1f));
            GUI.Label(new Rect(44f, 20f, 520f, 26f), "WORST CLASS EVER", _stTitle);
            GUI.Label(new Rect(46f, 48f, 560f, 18f), "external helper   ·   v9.0   ·   " + KeyName() + " 开合", _stSub);

            // 关闭
            Rect close = new Rect(p.width - 62f, 24f, 34f, 34f);
            bool chov = close.Contains(_mouse);
            if (chov) Box(close, _sPillOff);
            GUIStyle cs = chov ? Mk(16, new Color(1f, 0.45f, 0.48f, 1f)) : _stClose;
            cs.alignment = TextAnchor.MiddleCenter;
            GUI.Label(close, "×", cs);
            if (chov && _click) { Open = false; HackBehaviour.Menu = false; }

            // 两列胶囊行
            float top = 96f, rh = 50f, gY = 9f;
            float colW = (p.width - 60f - 26f) * 0.5f;
            int half = 14;
            for (int i = 0; i < T.Length; i++)
            {
                int col = i / half, row = i % half;
                float x = col == 0 ? 30f : 30f + colW + 26f;
                float y = top + row * (rh + gY);
                Rect r = new Rect(x, y, colW, rh);
                if (T[i].kind == 0) RowToggle(r, T[i].name, T[i].key, State(T[i].name), T[i].tip);
                else RowButton(r, T[i].name, T[i].tip, T[i].key);
            }

            // 底部状态
            float by = top + half * (rh + gY) + 8f;
            Unit m = null;
            try { m = UnitManager.Instance != null ? UnitManager.Instance.localPlayer : null; } catch { }
            string role = m == null ? "未进对局" : (m.IsTeacher ? "老师" : "学生");
            int nt = 0, ns = 0, nn = 0;
            try
            {
                UnitManager um = UnitManager.Instance;
                if (um != null && um.unitsDict != null)
                {
                    foreach (var kv in um.unitsDict)
                    {
                        Unit u = kv.Value;
                        if (u == null) continue;
                        int st = (int)u.UnitSubType;
                        if (st == 101) nt++; else if (st == 201) ns++; else if (st == 202) nn++;
                    }
                }
            }
            catch { }
            GUI.Label(new Rect(34f, by, 420f, 18f), "身份  " + role + "      场上  师 " + nt + " / 生 " + ns + " / NPC " + nn, _stTip);

            // 呼出键（点一下换键）
            Rect kb = new Rect(p.width - 176f, by - 6f, 132f, 30f);
            bool kbH = kb.Contains(_mouse);
            Box(kb, Binding ? _sCatOn : (kbH ? _sPillOn : _sPillOff));
            GUIStyle ks = Binding ? Mk(12, new Color(0.02f, 0.16f, 0.13f, 1f)) : _stKey;
            ks.alignment = TextAnchor.MiddleCenter;
            GUI.Label(kb, Binding ? "按下任意键…" : "呼出键  " + KeyName(), ks);
            if (kbH && _click && !Binding) { Binding = true; _bindAt = Time.unscaledTime; }

            GUI.Label(new Rect(p.width - 66f, by - 4f, 44f, 18f), ((int)(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime))).ToString(), _stKey);
        }

        /// <summary>v9.0：热键全部绑回来（鼠标点也行）</summary>
        public static void Hotkeys()
        {
            if (Binding) return;
            if (Input.GetKeyDown(KeyCode.F1)) ClickToggle("透视 ESP");
            if (Input.GetKeyDown(KeyCode.F12)) ClickToggle("骨骼透视");
            if (Input.GetKeyDown(KeyCode.F11)) ClickToggle("模型染色");
            if (Input.GetKeyDown(KeyCode.End)) ClickToggle("染色透墙");
            if (Input.GetKeyDown(KeyCode.F10)) ClickToggle("显示 NPC");
            if (Input.GetKeyDown(KeyCode.F9)) ClickToggle("显示名字");
            if (Input.GetKeyDown(KeyCode.F8)) ClickToggle("加速 x2.5");
            if (Input.GetKeyDown(KeyCode.F7)) ClickToggle("无限冲刺");
            if (Input.GetKeyDown(KeyCode.PageUp)) ClickToggle("御剑飞行");
            if (Input.GetKeyDown(KeyCode.H)) ClickToggle("第三人称");
            if (Input.GetKeyDown(KeyCode.B)) ClickToggle("自动旋转");
            if (Input.GetKeyDown(KeyCode.Home)) ClickToggle("无限体力");
            if (Input.GetKeyDown(KeyCode.F3)) ClickToggle("散射（霰弹）");
            if (Input.GetKeyDown(KeyCode.F4)) ClickToggle("速射 + 连发");
            if (Input.GetKeyDown(KeyCode.Alpha1)) ClickToggle("攻击穿墙");
            if (Input.GetKeyDown(KeyCode.Alpha7)) ClickToggle("准星锁定");
            if (Input.GetKeyDown(KeyCode.T)) ClickToggle("竹筒无限用");
            if (Input.GetKeyDown(KeyCode.Alpha6)) ClickToggle("刷闪光弹");
            if (Input.GetKeyDown(KeyCode.Alpha9)) ClickToggle("秒完成互动");
            if (Input.GetKeyDown(KeyCode.Alpha0)) ClickToggle("无限物品");
            if (Input.GetKeyDown(KeyCode.PageDown)) ClickToggle("无限投掷");
            if (Input.GetKeyDown(KeyCode.Alpha3)) ClickToggle("不可被抓");
            if (Input.GetKeyDown(KeyCode.Alpha4)) ClickToggle("不可被打晕");
            if (Input.GetKeyDown(KeyCode.Delete)) ClickToggle("完全隐形");
            if (Input.GetKeyDown(KeyCode.Y)) ClickToggle("自动刷新状态");
            if (Input.GetKeyDown(KeyCode.Alpha5)) ClickToggle("视角锁定");
            if (Input.GetKeyDown(KeyCode.G)) ClickToggle("防飞出地图");
            if (Input.GetKeyDown(KeyCode.Alpha8)) ClickToggle("击倒老师");
            if (Input.GetKeyDown(KeyCode.L)) ClickButton("放倒老师");
            if (Input.GetKeyDown(KeyCode.K)) ClickToggle("自动放倒");
            if (Input.GetKeyDown(KeyCode.M)) ClickToggle("缩小全场");
            if (Input.GetKeyDown(KeyCode.N)) ClickToggle("栽赃陷害");
            if (Input.GetKeyDown(KeyCode.O)) ClickToggle("抽屉塞竹筒");
            if (Input.GetKeyDown(KeyCode.F5)) ClickButton("传送到老师");
            if (Input.GetKeyDown(KeyCode.F6)) ClickButton("传送到学生");
            if (Input.GetKeyDown(KeyCode.Alpha2)) ClickButton("秒解救同伴");
            if (Input.GetKeyDown(KeyCode.Insert)) ClickButton("立即脱困");
            if (Input.GetKeyDown(KeyCode.U)) ClickButton("刷新状态一次");
        }

        /// <summary>行开关的当前状态</summary>
        public static bool State(string name)
        {
            if (name == "透视 ESP") return HackBehaviour.Esp;
            if (name == "骨骼透视") return HackBehaviour.Bones;
            if (name == "模型染色") return Skin.On;
            if (name == "染色透墙") return Skin.Wall;
            if (name == "显示 NPC") return HackBehaviour.ShowNpc;
            if (name == "加速 x2.5") return HackPatches.Speed;
            if (name == "无限冲刺") return HackPatches.InfSprint;
            if (name == "御剑飞行") return Fly.On;
            if (name == "第三人称") return ThirdPerson.On;
            if (name == "自动旋转") return AutoSpin.On;
            if (name == "无限体力") return Energy.On;
            if (name == "散射（霰弹）") return Shot.Scatter;
            if (name == "速射 + 连发") return Shot.Rapid;
            if (name == "攻击穿墙") return WallHack.On;
            if (name == "秒完成互动") return Instant.On;
            if (name == "无限物品") return Instant.InfItems;
            if (name == "竹筒无限用") return Taser.On;
            if (name == "显示名字") return NameTag.On;
            if (name == "准星锁定") return Selector.On;
            if (name == "刷闪光弹") return Flash.On;
            if (name == "击倒老师") return Knock.On;
            if (name == "自动放倒") return Slip.On;
            if (name == "缩小全场") return Mini.On;
            if (name == "栽赃陷害") return Frame.On;
            if (name == "抽屉塞竹筒") return DrawerHook.On;
            if (name == "无限投掷") return Stock.On;
            if (name == "不可被抓") return Protect.NoCatch;
            if (name == "不可被打晕") return Protect.NoStun;
            if (name == "完全隐形") return Ghost.On;
            if (name == "自动刷新状态") return Refresh.On;
            if (name == "视角锁定") return StableCam.On;
            if (name == "防飞出地图") return Safe.On;
            return false;
        }


        public static void Toggle()
        {
            Open = !Open;
            HackBehaviour.Menu = Open;
            if (Open) { _openAt = Time.unscaledTime; BuildNow(); if (!Glass.On) Glass.On = true; }
            else { _anim.Value = 0f; _anim.Target = 0f; _anim.Vel = 0f; }
        }

        public static void BuildNow() { _built = false; Build(); _built = true; }
    }



    /// <summary>v9.1：左上角常驻 HUD —— 只列当前开着的功能，iOS 白卡片风格</summary>
    public static class Hud
    {
        public static bool On = true;
        static Texture2D _bg, _dotOn;
        static GUIStyle _sBg, _sHead, _sItem;
        static bool _built;

        static void Build()
        {
            if (_built) return;
            _built = true;
            _bg = UiKit.Round(72, 24, new Color(1f, 1f, 1f, 0.93f), new Color(1f, 1f, 1f, 1f), new Color(0f, 0f, 0f, 0.05f), 1f);
            _dotOn = UiKit.Round(16, 8, new Color(0.204f, 0.780f, 0.349f, 1f), new Color(0.204f, 0.780f, 0.349f, 1f), new Color(0.204f, 0.780f, 0.349f, 1f), 0f);

            _sBg = new GUIStyle();
            _sBg.normal.background = _bg;
            _sBg.border = new RectOffset(24, 24, 24, 24);

            Font f = UiKit.F();
            _sHead = MkStyle(13, new Color(0.557f, 0.557f, 0.576f, 1f), f);
            _sHead.fontStyle = FontStyle.Bold;
            _sItem = MkStyle(14, new Color(0.110f, 0.110f, 0.118f, 1f), f);
        }

        static GUIStyle MkStyle(int size, Color c, Font f)
        {
            GUIStyle st = new GUIStyle();
            st.fontSize = size;
            st.normal.textColor = c;
            st.clipping = TextClipping.Overflow;
            st.alignment = TextAnchor.MiddleLeft;
            if (f != null) st.font = f;
            return st;
        }

        static string _flashName = "";
        static float _flashTill;

        /// <summary>切换功能时调一下，左上角对应那行会亮 1.2 秒</summary>
        public static void Flash(string name)
        {
            _flashName = name;
            _flashTill = Time.unscaledTime + 1.2f;
        }

        public static void Draw()
        {
            if (!On) return;
            Build();

            int n = 0;
            for (int i = 0; i < UiKit.T.Length; i++)
                if (UiKit.T[i].kind == 0 && UiKit.State(UiKit.T[i].name)) n++;
            if (n == 0) return;

            float w = 236f, rh = 22f;
            float h = 34f + n * rh + 6f;
            Rect p = new Rect(18f, 18f, w, h);

            GUI.Box(p, GUIContent.none, _sBg);
            Color oh = GUI.color;
            if (Time.unscaledTime < _flashTill) GUI.color = new Color(0f, 0.478f, 1f, 1f);
            GUI.Label(new Rect(p.x + 16f, p.y + 7f, w - 32f, 20f), "已开启  " + n + "  项", _sHead);
            GUI.color = oh;

            int k = 0;
            for (int i = 0; i < UiKit.T.Length; i++)
            {
                if (UiKit.T[i].kind != 0 || !UiKit.State(UiKit.T[i].name)) continue;
                float y = p.y + 32f + k * rh;
                bool hot = (UiKit.T[i].name == _flashName) && Time.unscaledTime < _flashTill;
                GUI.DrawTexture(new Rect(p.x + 17f, y + 7f, 8f, 8f), _dotOn);
                Color oc = GUI.color;
                if (hot) GUI.color = new Color(0f, 0.478f, 1f, 1f);
                GUI.Label(new Rect(p.x + 32f, y, w - 46f, 20f), UiKit.T[i].name, _sItem);
                GUI.color = oc;
                k++;
            }
        }
    }


    /// <summary>v9.4：竹筒（电击枪）无限次使用 —— 拦掉消耗 / 拦掉「用完通知」，并每帧把 spent 复位</summary>
    public static class Taser
    {
        public static bool On = true;
        public static string Last = "";

        static FieldInfo _fSpent, _fPending;
        static bool _cached;

        static void Cache()
        {
            if (_cached) return;
            _cached = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _fSpent = typeof(TaserComponent).GetField("spent", F);
            _fPending = typeof(TaserComponent).GetField("pending", F);
        }

        // 1) 不扣耐久、不置 spent
        public static bool Pre_Consume(TaserComponent __instance, Unit __0, ref bool __result)
        {
            if (!On || __instance == null) return true;
            __result = true;
            return false;
        }

        // 2) 不发 ItemUseCompleted_C2S（那是服务端回收物品的唯一入口）
        public static bool Pre_Complete(TaserComponent __instance, Unit __0)
        {
            if (!On || __instance == null) return true;
            return false;
        }

        // 3) MarkSpent 之后立刻复位
        public static void Post_Spent(TaserComponent __instance)
        {
            if (!On || __instance == null) return;
            Cache();
            try { if (_fSpent != null) _fSpent.SetValue(__instance, false); } catch { }
            try { if (_fPending != null) _fPending.SetValue(__instance, false); } catch { }
        }

        static void ResetOne(Unit u)
        {
            if (u == null) return;
            TaserComponent tc = null;
            try { tc = u.GetComponent<TaserComponent>(); } catch { }
            if (tc == null) return;
            Cache();
            try { if (_fSpent != null && (bool)_fSpent.GetValue(tc)) _fSpent.SetValue(tc, false); } catch { }
            try { if (_fPending != null && (bool)_fPending.GetValue(tc)) _fPending.SetValue(tc, false); } catch { }
            try
            {
                ItemData d = u.GetData<ItemData>();
                if (d != null && d.CurrentDurability < 999f) d.CurrentDurability = 9999f;
            }
            catch { }
        }

        public static void Tick()
        {
            if (!On) return;
            UnitManager um = null;
            try { um = UnitManager.Instance; } catch { }
            if (um == null || um.unitsDict == null) return;
            List<Unit> list = new List<Unit>(um.unitsDict.Values);
            for (int i = 0; i < list.Count; i++) ResetOne(list[i]);
        }
    }


    // ================= v9.5 恢复：之前做过的全部功能 =================

    /// <summary>击倒老师：直接改 TeacherStunComponent 的内部状态（动画层面，服务端不认，仅本地表现）</summary>
    public static class Knock
    {
        public static bool On = false;
        public static bool UseSlip = true;
        public static float Interval = 1.2f;
        public static string Last = "";

        static FieldInfo _fRemaining, _fSlipping, _fProjStun;
        static MethodInfo _mVisuals;
        static bool _cached;
        static float _next;
        static int _cnt;

        static void Cache()
        {
            if (_cached) return;
            _cached = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _fRemaining = typeof(TeacherStunComponent).GetField("remaining", F);
            _fSlipping  = typeof(TeacherStunComponent).GetField("slipping", F);
            _fProjStun  = typeof(TeacherStunComponent).GetField("projectileStun", F);
            _mVisuals   = typeof(TeacherStunComponent).GetMethod("ApplyStunVisuals", F);
        }

        public static bool Down(TeacherStunComponent st)
        {
            if (st == null) return false;
            Cache();
            try { if (_fProjStun != null) _fProjStun.SetValue(st, false); } catch { }
            try { if (_fSlipping != null) _fSlipping.SetValue(st, UseSlip); } catch { }
            try { if (_fRemaining != null) _fRemaining.SetValue(st, 9999f); } catch { }
            try { if (_mVisuals != null) _mVisuals.Invoke(st, null); } catch { }
            return true;
        }

        public static void Do()
        {
            UnitManager um = null;
            try { um = UnitManager.Instance; } catch { }
            if (um == null || um.unitsDict == null) return;
            List<Unit> list = new List<Unit>(um.unitsDict.Values);
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Unit u = list[i];
                if (u == null || u.IsDead || !u.IsTeacher) continue;
                TeacherStunComponent st = null;
                try { st = u.GetComponent<TeacherStunComponent>(); } catch { }
                if (st != null && Down(st)) n++;
            }
            _cnt += n;
            Last = "击倒 " + _cnt;
        }

        public static void Tick()
        {
            if (!On) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            Do();
        }
    }

    /// <summary>缩小全场模型（自己和别人一起变小）</summary>
    public static class Mini
    {
        public static bool On = false;
        public static float Scale = 0.15f;
        public static string Last = "";
        static readonly Dictionary<ulong, Vector3> _orig = new Dictionary<ulong, Vector3>();
        static float _next;

        static Transform Root(Unit u)
        {
            try { return u.model != null ? u.model.transform : u.transform; } catch { return null; }
        }

        public static void Tick()
        {
            UnitManager um = null;
            try { um = UnitManager.Instance; } catch { }
            if (um == null || um.unitsDict == null) return;

            if (On)
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.30f;
                List<Unit> list = new List<Unit>(um.unitsDict.Values);
                for (int i = 0; i < list.Count; i++)
                {
                    Transform t = Root(list[i]);
                    if (t == null) continue;
                    if (!_orig.ContainsKey(list[i].UID)) _orig[list[i].UID] = t.localScale;
                    t.localScale = new Vector3(Scale, Scale, Scale);
                }
                Last = "缩小 " + _orig.Count;
            }
            else if (_orig.Count > 0)
            {
                foreach (KeyValuePair<ulong, Vector3> kv in _orig)
                {
                    Unit u = null;
                    try { u = um.GetUnitByUID(kv.Key); } catch { }
                    Transform t = u != null ? Root(u) : null;
                    if (t != null) t.localScale = kv.Value;
                }
                _orig.Clear();
                Last = "已还原";
            }
        }
    }

    /// <summary>准星锁定圈：屏幕中央 FOV 圆内自动选目标</summary>
    public static class Selector
    {
        public static bool On = false;
        public static float FovRadius = 150f;
        public static bool Occlude = true;
        public static float MaxDist = 140f;
        public static Unit Cur;
        public static Vector3 CurPoint;

        static Texture2D _ring;
        static float _next;

        static void Ring()
        {
            if (_ring != null) return;
            int N = 96, S = N * 4;
            _ring = new Texture2D(S, S, TextureFormat.ARGB32, false);
            _ring.hideFlags = HideFlags.HideAndDontSave;
            Color[] px = new Color[S * S];
            float c = (S - 1) * 0.5f, R = c - 2f, w = 2.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * S + x] = (Mathf.Abs(d - R) <= w) ? new Color(0.35f, 0.88f, 0.78f, 0.85f) : new Color(0, 0, 0, 0);
                }
            _ring.SetPixels(px);
            _ring.Apply();
        }

        static Vector3 Anchor(Unit u)
        {
            try { return u.model != null ? u.model.transform.position + Vector3.up * 1.1f : u.Position + Vector3.up * 1.1f; }
            catch { return u.Position; }
        }

        public static void Tick()
        {
            if (!On) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.05f;

            Camera cam = Camera.main;
            if (cam == null) return;
            UnitManager um = null;
            try { um = UnitManager.Instance; } catch { }
            if (um == null || um.unitsDict == null) return;

            Unit best = null;
            float bestScore = -1f;
            Vector3 bestPt = Vector3.zero;
            List<Unit> list = new List<Unit>(um.unitsDict.Values);
            for (int i = 0; i < list.Count; i++)
            {
                Unit u = list[i];
                if (u == null || u.isLocalPlayer || u.IsDead) continue;
                int st = (int)u.UnitSubType;
                if (st != 101 && st != 201 && st != 202) continue;
                Vector3 ap = Anchor(u);
                float dist = Vector3.Distance(cam.transform.position, ap);
                if (dist > MaxDist) continue;
                Vector3 sp = cam.WorldToScreenPoint(ap);
                if (sp.z <= 0f) continue;
                float px = Mathf.Sqrt((sp.x - Screen.width * 0.5f) * (sp.x - Screen.width * 0.5f) +
                                      (sp.y - Screen.height * 0.5f) * (sp.y - Screen.height * 0.5f));
                if (px > FovRadius) continue;
                if (Occlude)
                {
                    RaycastHit hit;
                    Vector3 d = ap - cam.transform.position;
                    if (Physics.Raycast(cam.transform.position, d.normalized, out hit, d.magnitude - 0.25f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.transform != u.transform && (u.model == null || !hit.transform.IsChildOf(u.model.transform)))
                            continue;
                }
                float ss = 1f - px / Mathf.Max(1f, FovRadius);
                float ds = 1f - dist / Mathf.Max(1f, MaxDist);
                float score = ss * 0.7f + ds * 0.3f;
                if (score > bestScore) { bestScore = score; best = u; bestPt = ap; }
            }
            Cur = best;
            CurPoint = bestPt;
        }

        public static void Draw()
        {
            if (!On) return;
            Ring();
            float d = FovRadius * 2f;
            float x = Screen.width * 0.5f - FovRadius;
            float y = Screen.height * 0.5f - FovRadius;
            Color oc = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Cur != null ? 1f : 0.35f);
            GUI.DrawTexture(new Rect(x, y, d, d), _ring);
            GUI.color = oc;
        }
    }

    /// <summary>栽赃陷害：让一个别人原地旋转 + 上下飘，看起来像在开挂</summary>
    public static class Frame
    {
        public static bool On = false;
        public static string Last = "";
        public static ulong TargetUID;
        public static float SpinSpeed = 1400f;
        public static float LiftStep = 0.9f;
        public static float LiftMax = 18f;

        static float _lift;
        static uint _epoch;
        static float _next;

        static List<Unit> Candidates()
        {
            List<Unit> r = new List<Unit>();
            UnitManager um = null;
            try { um = UnitManager.Instance; } catch { }
            if (um == null || um.unitsDict == null) return r;
            List<Unit> list = new List<Unit>(um.unitsDict.Values);
            for (int i = 0; i < list.Count; i++)
            {
                Unit u = list[i];
                if (u == null || !u.isPlayer || u.isLocalPlayer || u.IsDead) continue;
                r.Add(u);
            }
            return r;
        }

        public static void PickNext()
        {
            List<Unit> c = Candidates();
            if (c.Count == 0) { TargetUID = 0UL; Last = "没有可选目标"; return; }
            int idx = 0;
            for (int i = 0; i < c.Count; i++) if (c[i].UID == TargetUID) { idx = (i + 1) % c.Count; break; }
            TargetUID = c[idx].UID;
            Last = "目标 " + c[idx].UID;
        }

        public static void Tick()
        {
            if (!On) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.35f;

            UnitManager um = null;
            try { um = UnitManager.Instance; } catch { }
            if (um == null || um.unitsDict == null) return;

            Unit t = null;
            try { t = um.GetUnitByUID(TargetUID); } catch { }
            if (t == null || t.isLocalPlayer || t.IsDead) { PickNext(); return; }

            _lift += LiftStep;
            if (_lift > LiftMax) _lift = 0f;
            _epoch++;

            Vector3 p = t.Position;
            Vector3 fake = new Vector3(p.x, p.y + _lift, p.z);
            try { t.SetPosition(fake); } catch { }
            try
            {
                LocomotionComponent lc = t.GetComponent<LocomotionComponent>();
                if (lc != null) lc.SetPosition(fake);
            }
            catch { }
            try { t.transform.rotation = Quaternion.Euler(0f, 720f * Time.time, 0f); } catch { }
            Last = "lift=" + _lift.ToString("0.0");
        }
    }

    /// <summary>刷闪光弹：在自己脚底下生成</summary>
    public static class Flash
    {
        public static bool On = false;
        public static int Count = 2;
        public static string Last = "";
        static int _id = -1;
        static float _next;

        static int Id()
        {
            if (_id > 0) return _id;
            try
            {
                cfg.TbUnit tb = cfg.Tables.TbUnit;
                List<cfg.Unit> list = tb.DataList;
                for (int i = 0; i < list.Count; i++)
                    if ((int)list[i].SubType == (int)EUnitSubType.Flashbang) { _id = list[i].Id; break; }
            }
            catch { }
            return _id;
        }

        public static void Do()
        {
            Unit me = null;
            UnitManager um = null;
            try { um = UnitManager.Instance; me = um.localPlayer; } catch { }
            if (me == null) return;
            int id = Id();
            if (id <= 0) { Last = "找不到闪光弹 id"; return; }
            Vector3 at = me.Position + Vector3.up * 0.15f;
            int n = 0;
            for (int i = 0; i < Mathf.Clamp(Count, 1, 10); i++)
            {
                try { um.CreateCommonUnitDirectly(id, at, Vector3.zero, null, true); n++; } catch { }
            }
            Last = "刷了 " + n + " 颗 id=" + id;
        }

        public static void Tick()
        {
            if (!On) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.60f;
            Do();
        }
    }

    /// <summary>抽屉塞竹筒：每个抽屉打开时塞一根电击枪（竹筒）</summary>
    public static class DrawerHook
    {
        public static bool On = false;
        public static string Last = "";
        static int _id = -1;
        static MethodInfo _mAdd;
        static bool _cached;

        static int Id()
        {
            if (_id > 0) return _id;
            try
            {
                cfg.TbUnit tb = cfg.Tables.TbUnit;
                List<cfg.Unit> list = tb.DataList;
                for (int i = 0; i < list.Count; i++)
                    if ((int)list[i].SubType == (int)EUnitSubType.Taser) { _id = list[i].Id; break; }
            }
            catch { }
            return _id;
        }

        static void Cache()
        {
            if (_cached) return;
            _cached = true;
            try
            {
                _mAdd = typeof(DrawerComponent).GetMethod("AddItemAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(int) }, null);
            }
            catch { }
        }

        public static bool Fill(DrawerComponent d)
        {
            if (d == null) return false;
            Cache();
            int id = Id();
            if (id <= 0 || _mAdd == null) return false;
            try { _mAdd.Invoke(d, new object[] { id }); return true; }
            catch { return false; }
        }

        public static void FillAll()
        {
            UnitManager um = null;
            try { um = UnitManager.Instance; } catch { }
            if (um == null || um.unitsDict == null) return;
            List<Unit> list = new List<Unit>(um.unitsDict.Values);
            int ok = 0, tot = 0;
            for (int i = 0; i < list.Count; i++)
            {
                DrawerComponent d = null;
                try { d = list[i].GetComponent<DrawerComponent>(); } catch { }
                if (d == null) continue;
                tot++;
                if (Fill(d)) ok++;
            }
            Last = "抽屉 " + ok + "/" + tot;
        }

        // DrawerComponent.OnActive 的 postfix：打开抽屉就塞
        public static void Post_Active(DrawerComponent __instance)
        {
            if (!On) return;
            if (Fill(__instance)) { Last = "塞了竹筒"; }
        }
    }

    /// <summary>放倒老师：走香蕉皮的服务端通道（老师本机会真的滑倒）</summary>
    public static class Slip
    {
        public static bool On = false;
        public static float Interval = 2.0f;
        public static string Last = "";
        static float _next;

        static Unit NearTeacher()
        {
            UnitManager um = null;
            Unit me = null;
            try { um = UnitManager.Instance; me = um.localPlayer; } catch { }
            if (um == null || me == null || um.unitsDict == null) return null;
            Unit best = null;
            float bd = 1e9f;
            List<Unit> list = new List<Unit>(um.unitsDict.Values);
            for (int i = 0; i < list.Count; i++)
            {
                Unit u = list[i];
                if (u == null || !u.IsTeacher || u.IsDead) continue;
                float d = Vector3.Distance(u.Position, me.Position);
                if (d < bd) { bd = d; best = u; }
            }
            return best;
        }

        public static void Force(Unit t)
        {
            if (t == null) return;
            // GetSlip 第一行就会 return（projectileStun 为真），先清掉
            try
            {
                TeacherStunComponent st = t.GetComponent<TeacherStunComponent>();
                if (st != null)
                {
                    FieldInfo f = typeof(TeacherStunComponent).GetField("projectileStun",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null) f.SetValue(st, false);
                }
            }
            catch { }

            BananaSlip_S2C msg = new BananaSlip_S2C();
            msg.playerUID = t.UID;
            bool host = false;
            try { host = Mirror.NetworkServer.active; } catch { }
            try
            {
                RoomManager rm = MonoSingletonT<RoomManager>.Instance;
                if (host) rm.BroadcastToAll<BananaSlip_S2C>(msg);
                else rm.SendToServer<BananaSlip_S2C>(msg, 0);
            }
            catch { }
            try { BananaComponent.ApplySlip(msg); } catch { }
            Last = "放倒 " + t.UID;
        }

        public static void Do()
        {
            Unit t = NearTeacher();
            if (t == null) { Last = "附近没有老师"; return; }
            Force(t);
        }

        public static void Tick()
        {
            if (!On) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            Do();
        }
    }

    /// <summary>名字标签：显示 / 隐藏所有人头顶名字</summary>
    public static class NameTag
    {
        public static bool On = true;
        public static string Last = "";
        static FieldInfo _fName, _fShow;
        static bool _cached;

        static void Cache()
        {
            if (_cached) return;
            _cached = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _fName = typeof(NameLabelComponent).GetField("mNameText", F);
            _fShow = typeof(NameLabelComponent).GetField("mHasVisibleName", F);
        }

        public static void Apply(Unit u)
        {
            if (u == null) return;
            Cache();
            NameLabelComponent nl = null;
            try { nl = u.GetComponent<NameLabelComponent>(); } catch { }
            if (nl == null) return;
            try
            {
                object o = _fName != null ? _fName.GetValue(nl) : null;
                Component comp = o as Component;      // TextMeshPro 也是 Component，避免引用 Unity.TextMeshPro.dll
                if (comp != null && comp.gameObject.activeSelf != On) comp.gameObject.SetActive(On);
            }
            catch { }
        }

        public static void Tick()
        {
            UnitManager um = null;
            try { um = UnitManager.Instance; } catch { }
            if (um == null || um.unitsDict == null) return;
            List<Unit> list = new List<Unit>(um.unitsDict.Values);
            for (int i = 0; i < list.Count; i++) Apply(list[i]);
        }
    }

    public static class HackPatches
    {
        // v4.9 无限投掷物：扔完不去扣"补货权重"，配合 Stock.ClearCooldown 就能一直扔
        public static bool Pre_RefillConsumed()
        {
            return !Stock.On;
        }

        // v4.8 秒完成：把"使用时长"和"取货按住时长"直接压成 0.01 秒
        public static bool Pre_UseDuration(ref float __result)
        {
            if (!Instant.On) return true;
            __result = 0.01f;
            return false;
        }

        public static bool Pre_HoldDuration(ref float __result)
        {
            if (!Instant.On) return true;
            __result = 0.01f;
            return false;
        }

        public static bool InfSprint = true;
        public static bool Speed = false;
        public static float SpeedMult = 2.5f;

        static HarmonyLib.Harmony _h;
        static bool _done;
        static FieldInfo _fShot, _fDir, _fTeacher;

        public static void Install()
        {
            if (_done) return;
            _done = true;
            _h = new HarmonyLib.Harmony("wce.hack");

            // ---- 数值类 ----
            PatchGetter(typeof(TeacherSprintComponent), "EnergyBlocksSprint", "Post_EnergyBlocks");
            PatchGetter(typeof(TeacherSprintComponent), "CanSprint", "Post_CanSprint");
            PatchGetter(typeof(TeacherSprintComponent), "ShouldDrainSprintEnergy", "Post_Drain");
            // v1.8：加速改挂在 InputComponent.GetCurrentMoveSpeed()（真正决定移动速度的函数，
            // 而且能拿到 owner 只改自己）；以前挂 MovementSetting.runningFactor 只在"跑"的时候乘，
            // 走路完全没变化，所以看起来"加速没效果"。
            Patch(typeof(InputComponent), "GetCurrentMoveSpeed", null, null, "Post_MoveSpeed");

            _fTeacher = typeof(TeacherMeleeComponent).GetField("teacher", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _fShot = typeof(TeacherSlashProjectile).GetField("shot", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _fDir = typeof(TeacherSlashFired_S2C).GetField("direction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // ---- 剑气：发射瞬间锁定 (host 也是这里生成 S2C) ----
            Patch(typeof(TeacherMeleeComponent), "CreateShot", new Type[] { typeof(TeacherSlashFire_C2S) },
                  null, "Post_CreateShot");
            // ---- 剑气/黑板擦：本地生成投射物时兜底改方向 ----
            Patch(typeof(TeacherSlashProjectile), "ApplyFire", new Type[] { typeof(TeacherSlashFired_S2C) },
                  "Pre_ApplyFire", null);
            // ---- 黑板擦：host 构造 S2C 时改方向 ----
            Patch(typeof(SkillComponent), "TryCreateEraserShot",
                  new Type[] { typeof(TeacherSlashFire_C2S), typeof(TeacherSlashFired_S2C).MakeByRefType() },
                  null, "Post_EraserShot");
            // ---- 剑气飞行中每帧转向 (真追踪) ----
            Patch(typeof(TeacherSlashProjectile), "AdvanceOwned", new Type[] { typeof(float) },
                  "Pre_AdvanceOwned", null);
            // ---- 粉笔：生成数据时把方向改成朝学生 ----
            Patch(typeof(SkillComponent), "OnRefAction", new Type[] { typeof(UnitDataNet).MakeByRefType() },
                  null, "Post_OnRefAction");
            // ---- 粉笔等所有抛掷物：飞行中每帧转向 ----
            Patch(typeof(ProjectileComponent), "OnFixedUpdate", new Type[] { typeof(float) },
                  "Pre_ProjFixed", null);

            // ---- v1.4 散射 / 速射 / 无限体力 ----
            Patch(typeof(SkillComponent), "ShootChalk",
                  new Type[] { typeof(int), typeof(float), typeof(float) },
                  null, "Post_ShootChalk");
            Patch(typeof(SkillComponent), "TryUseAttackEnergy",
                  new Type[] { typeof(ERoomRuleId) },
                  "Pre_TryUseAttackEnergy", null);
            Patch(typeof(SkillComponent), "DealSkillEffect",
                  new Type[] { },
                  "Pre_DealSkillEffect", null);
            // ---- v1.6 无限技能能量（只对自己）----
            PatchGetter(typeof(SkillComponent), "HasInfiniteSkillEnergy", "Post_HasInfiniteEnergy");
            // ---- v2.0 穿墙：黑板擦/剑气飞行检测的 mask ----
            var fq = typeof(Unit).Assembly.GetType("ProjectileFlightQuery");
            if (fq != null) Patch(fq, ".ctor", null, "Pre_FlightMask", null);
            else HackPlugin.Log("[WceHack] 没有 ProjectileFlightQuery 类型");
        }

        public static void Patch(Type t, string name, Type[] argTypes, string pre, string post)
        {
            try
            {
                MethodBase mi = null;
                const BindingFlags BF = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                if (name == ".ctor")
                {
                    // 构造函数不能用 GetMethod(".ctor") 拿，得走 GetConstructors
                    var cs = t.GetConstructors(BF);
                    if (cs.Length > 0) mi = cs[0];
                }
                else if (argTypes == null) mi = t.GetMethod(name, BF);
                else mi = t.GetMethod(name, BF, null, argTypes, null);
                if (mi == null) { HackPlugin.Log("no method " + t.Name + "." + name); return; }
                var preM = pre == null ? null : FindPatch(pre);
                var postM = post == null ? null : FindPatch(post);
                if (pre != null && preM == null) { HackPlugin.Log("no prefix " + pre); return; }
                if (post != null && postM == null) { HackPlugin.Log("no postfix " + post); return; }
                _h.Patch(mi,
                    preM == null ? null : new HarmonyLib.HarmonyMethod(preM),
                    postM == null ? null : new HarmonyLib.HarmonyMethod(postM));
                HackPlugin.Log("patched " + t.Name + "." + name);
            }
            catch (Exception e) { HackPlugin.Log("patch err " + name + ": " + e.Message); }
        }

        // 补丁方法可能写在任意一个 hack 类里，全都找一遍
        static MethodInfo FindPatch(string name)
        {
            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type[] hosts = new Type[]
            {
                typeof(HackPatches), typeof(Shot), typeof(Aim), typeof(Skin),
                typeof(Escape), typeof(Ghost), typeof(Energy), typeof(WallHack), typeof(Protect), typeof(Rescue), typeof(StableCam), typeof(Safe), typeof(Instant), typeof(Stock), typeof(Fly),  typeof(Pillar), typeof(Protect), typeof(Taser), typeof(DrawerHook), typeof(HackBehaviour)
            };
            for (int i = 0; i < hosts.Length; i++)
            {
                MethodInfo m = null;
                try { m = hosts[i].GetMethod(name, F); } catch { }
                if (m != null) return m;
            }
            return null;
        }

        public static void RegisterTaser()
        {
            Patch(typeof(TaserComponent), "TryConsumeShot", new Type[] { typeof(Unit) }, "Pre_Consume", null);
            Patch(typeof(TaserComponent), "CompleteShot", new Type[] { typeof(Unit) }, "Pre_Complete", null);
            Patch(typeof(TaserComponent), "MarkSpent", null, null, "Post_Spent");
        }

        public static void PatchGetter(Type t, string prop, string postName)
        {
            var p = t.GetProperty(prop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) { HackPlugin.Log("no prop " + t.Name + "." + prop); return; }
            var mi = p.GetGetMethod(true);
            var pi = FindPatch(postName);
            if (mi == null) { HackPlugin.Log("no getter " + t.Name + "." + prop); return; }
            if (pi == null) { HackPlugin.Log("no postfix " + postName); return; }
            try
            {
                _h.Patch(mi, null, new HarmonyLib.HarmonyMethod(pi));
                HackPlugin.Log("patched " + t.Name + "." + prop);
            }
            catch (Exception e) { HackPlugin.Log("patch err " + prop + ": " + e.Message); }
        }

        // 无限技能能量（HasInfiniteSkillEnergy 的 getter）—— 只对自己
        public static void Post_HasInfiniteEnergy(SkillComponent __instance, ref bool __result)
        {
            if (!Energy.On) return;
            try
            {
                var o = __instance.owner;
                if (o != null && o.isLocalPlayer) __result = true;
            }
            catch { }
        }

        // ===== sprint / speed =====
        // ---- 冲刺：只对本地自己生效 ----
        static bool Me(TeacherSprintComponent c)
        {
            try
            {
                var o = c.owner;
                return o != null && o.isLocalPlayer;
            }
            catch { return false; }
        }

        public static void Post_EnergyBlocks(TeacherSprintComponent __instance, ref bool __result)
        { if (InfSprint && Me(__instance)) __result = false; }

        public static void Post_CanSprint(TeacherSprintComponent __instance, ref bool __result)
        { if (InfSprint && Me(__instance)) __result = true; }

        public static void Post_Drain(TeacherSprintComponent __instance, ref bool __result)
        { if (InfSprint && Me(__instance)) __result = false; }
        public static void Post_Mult(ref float __result)
        {
            if (!Speed) return;
            if (__result <= 0f || __result > 50f) return;
            __result *= SpeedMult;
        }

        // v1.8：真正的移动速度（InputComponent 每帧算一次，自己 → 只改自己）
        static float _spdLog;
        public static void Post_MoveSpeed(InputComponent __instance, ref float __result)
        {
            if (!Speed) return;
            if (__result <= 0f || __result > 60f) return;
            try
            {
                var o = __instance.owner;
                if (o == null || !o.isLocalPlayer) return;
            }
            catch { return; }
            float before = __result;
            __result *= SpeedMult;
            float now = Time.unscaledTime;
            if (now - _spdLog > 2f)
            {
                _spdLog = now;
                HackPlugin.Log("speed " + before.ToString("0.00") + " -> " + __result.ToString("0.00"));
            }
        }

        // ===== sword slash =====
        static ulong TeacherUid(TeacherMeleeComponent inst)
        {
            try
            {
                if (_fTeacher != null)
                {
                    var t = _fTeacher.GetValue(inst) as Unit;
                    if (t != null) return t.UID;
                }
            }
            catch { }
            try { return inst.owner.UID; } catch { }
            return 0;
        }

        public static void Post_CreateShot(TeacherMeleeComponent __instance, ref TeacherSlashFired_S2C __result)
        {
            if (!Aim.AutoAim) return;
            try
            {
                Vector3 from = __result.origin;
                ulong ex = TeacherUid(__instance);
                Vector3 aim;
                Unit tgt;
                if (!Aim.AimPoint(from, __result.speed, ex, out aim, out tgt)) return;
                Vector3 d = aim - from;
                if (d.sqrMagnitude < 0.04f) return;
                __result.direction = d.normalized;
            }
            catch { }
        }

        public static void Pre_ApplyFire(ref TeacherSlashFired_S2C __0)
        {
            if (!Aim.AutoAim) return;
            try
            {
                Vector3 from = __0.origin;
                Vector3 aim;
                Unit tgt;
                if (!Aim.AimPoint(from, __0.speed, __0.teacherUID, out aim, out tgt)) return;
                Vector3 d = aim - from;
                if (d.sqrMagnitude < 0.04f) return;
                __0.direction = d.normalized;
            }
            catch { }
        }

        public static void Post_EraserShot(ref TeacherSlashFired_S2C __1)
        {
            if (!Aim.AutoAim) return;
            try
            {
                Vector3 from = __1.origin;
                Vector3 aim;
                Unit tgt;
                if (!Aim.AimPoint(from, __1.speed, __1.teacherUID, out aim, out tgt)) return;
                Vector3 d = aim - from;
                if (d.sqrMagnitude < 0.04f) return;
                __1.direction = d.normalized;
            }
            catch { }
        }

        // 剑气飞行中每帧朝学生转向（反射写 private struct 字段 shot.direction）
        public static void Pre_AdvanceOwned(TeacherSlashProjectile __instance)
        {
            if (!Aim.AutoAim || !Aim.Homing) return;
            if (_fShot == null || _fDir == null) return;
            try
            {
                object boxed = _fShot.GetValue(__instance);
                if (boxed == null) return;
                var shot = (TeacherSlashFired_S2C)boxed;
                Vector3 cur = __instance.transform.position;

                bool ok;
                Vector3 want = Aim.DirLead(cur, shot.speed, out ok, shot.teacherUID);
                if (!ok) return;
                Vector3 dir = Aim.Turn(shot.direction, want, Time.fixedDeltaTime);
                _fDir.SetValue(boxed, dir);
                _fShot.SetValue(__instance, boxed);
            }
            catch { }
        }

        // ===== chalk =====
        public static void Post_OnRefAction(SkillComponent __instance, ref UnitDataNet __0)
        {
            if (!Aim.AutoAim) return;
            try
            {
                ulong creator = __0.CreaterUID;
                Vector3 from;
                try { from = __instance.owner.Position; } catch { from = Vector3.zero; }
                if (from == Vector3.zero) return;
                bool ok;
                Vector3 dir = Aim.DirLead(from, __0.ShootSpeed, out ok, creator);
                if (!ok) return;
                __0.ShootForward = dir;
                HackPlugin.Log("chalk locked");
            }
            catch { }
        }

        // 所有 ProjectileComponent 抛掷物飞行中追踪（含平滑转向）
        public static void Pre_ProjFixed(ProjectileComponent __instance)
        {
            if (!Aim.AutoAim || !Aim.Homing) return;
            try
            {
                Vector3 cur;
                try { cur = __instance.owner.Position; } catch { return; }
                if (cur.x == 0f && cur.y == 0f && cur.z == 0f) return;
                var nd = __instance.netData;

                bool ok;
                Vector3 want = Aim.DirLead(cur, nd.ShootSpeed, out ok, nd.CreaterUID);
                if (!ok) return;
                __instance.netData.ShootForward = Aim.Turn(nd.ShootForward, want, Time.fixedDeltaTime);
            }
            catch { }
        }
    }

    // ---------- esp / menu (chinese) ----------
    public class HackBehaviour : MonoBehaviour
    {
        public static bool Esp = true;
        public static bool Menu = true;
        static float _flyKey, _stockKey, _f4Key, _rKey, _oKey, _pKey, _insKey, _menuKey;
        public static bool ShowNpc = false;
        public static bool Tracers = true;
        public static float MaxDist = 250f;

        static Texture2D _tex;
        static Font _cnFont;
        static GUIStyle _lbl, _big, _small, _tag;
        static readonly List<Unit> _buf = new List<Unit>();
        static string _msg = "";
        static float _msgTill;

        public static Texture2D White()
        {
            if (_tex == null)
            {
                _tex = new Texture2D(1, 1);
                _tex.SetPixel(0, 0, Color.white);
                _tex.Apply();
            }
            return _tex;
        }

        static Font CnFont()
        {
            if (_cnFont != null) return _cnFont;
            try
            {
                _cnFont = Font.CreateDynamicFontFromOSFont(
                    new string[] { "Microsoft YaHei", "微软雅黑", "Microsoft YaHei UI", "SimHei", "黑体", "SimSun", "宋体", "Arial" }, 16);
            }
            catch { }
            return _cnFont;
        }

        // 文件触发兜底：往 <游戏>\Mods\wcehack\cmd.txt 写一行命令，0.5s 内执行并删掉文件。
        // 键盘注入/布局不灵时用它，用法： teacher / esp / ghost / escape / speed
        static float _trigAt;
        void CheckTrigger()
        {
            if (Time.unscaledTime - _trigAt < 0.5f) return;
            _trigAt = Time.unscaledTime;
            try
            {
                string dir = System.IO.Path.Combine(Application.dataPath, "..", "Mods", "wcehack");
                string p = System.IO.Path.Combine(dir, "cmd.txt");
                if (!System.IO.File.Exists(p)) return;
                string cmd = System.IO.File.ReadAllText(p).Trim().ToLowerInvariant();
                System.IO.File.Delete(p);
                Unit m = null;
                try { m = UnitManager.Instance.localPlayer; } catch { }
                if (cmd == "wall") { WallHack.On = !WallHack.On; Say(WallHack.On ? "穿墙 开" : "穿墙 关"); }
                else if (cmd == "esp") { Esp = !Esp; Say(Esp ? "透视 开" : "透视 关"); }
                else if (cmd == "ghost") { Ghost.On = !Ghost.On; Say(Ghost.On ? "隐形 开" : "隐形 关"); }
                else if (cmd == "escape") { bool ok = Escape.DoEscape(m); Say(ok ? "脱困执行" : "脱困：没有本地玩家"); }
                else if (cmd == "speed") { HackPatches.Speed = !HackPatches.Speed; Say(HackPatches.Speed ? "加速 开" : "加速 关"); }
                else if (cmd == "who") { Say(m == null ? "没有本地玩家" : ("我是 " + (m.IsTeacher ? "老师" : "学生") + " SubType=" + (int)m.UnitSubType + " UID=" + m.UID)); }
                else Say("未知命令 " + cmd);
            }
            catch (Exception e) { HackPlugin.Log("[WceHack] trigger 失败 " + e.Message); }
        }

void LateUpdate()
{
    Fly.LateTick();
    ThirdPerson.LateTick();
    // v8.2: 渲染结束后抓一帧做玻璃背景；菜单开着时再压一次光标解锁（游戏 CameraCtr 每帧会抢回去）
    Glass.Tick();
    if (UiKit.Open) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
}

        void Update()
        {
            CheckTrigger();
            UiKit.Hotkeys();   // v9.0: 热键全部绑回来
            if (UiKit.Open) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }

            // v8.3：全游戏只保留一个可自定义的菜单键，其余热键全部取消
            if (!UiKit.Binding && Input.GetKeyDown(UiKit.MenuKey) && Time.unscaledTime - _menuKey > 0.25f)
            {
                _menuKey = Time.unscaledTime;
                UiKit.Toggle();
            }

            Shot.Tick();
            Energy.Tick();
            Skin.Tick();
            WallHack.Tick();
            Protect.Tick();
            StableCam.Tick();
            Escape.Tick();
            Safe.Tick();
            Instant.Tick();
            Stock.Tick();
            Fly.Tick();
            Rescue.Tick();
            Refresh.Tick();
            Pillar.Tick();
            Taser.Tick();
            Knock.Tick();
            Mini.Tick();
            Selector.Tick();
            Flash.Tick();
            Slip.Tick();
            Frame.Tick();
            NameTag.Tick();
            Ghost.Tick();
            AutoSpin.Tick();
        }

        static void Say(string m)
        {
            _msg = m;
            _msgTill = Time.realtimeSinceStartup + 2.5f;
            HackPlugin.Log(m);
        }

        static UnitManager UM()
        {
            try { return UnitManager.Instance; }
            catch { return null; }
        }

        static void ToggleDebugNames()
        {
            try
            {
                var r = GameRoot.Inst;
                if (r == null) { Say("GameRoot 为空"); return; }
                bool cur = r.debug_showName;
                var f = typeof(GameRoot).GetField("<debug_showName>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (f == null) { Say("找不到字段"); return; }
                f.SetValue(r, !cur);
                Say("显示名字 " + (r.debug_showName ? "开" : "关"));
            }
            catch (Exception e) { Say("出错 " + e.Message); }
        }

        static void DoTeleport(bool toTeacher)
        {
            var um = UM();
            if (um == null) { Say("还没有进对局"); return; }
            var me = um.localPlayer;
            if (me == null) { Say("本机角色为空"); return; }
            Unit best = null;
            float bd = float.MaxValue;
            Vector3 mp = me.Position;
            Collect(um);
            for (int i = 0; i < _buf.Count; i++)
            {
                var u = _buf[i];
                if (u == null || u == me) continue;
                int sub;
                try { sub = (int)u.UnitSubType; } catch { continue; }
                bool match = toTeacher ? (sub == 101) : (sub == 201);
                if (!match) continue;
                float d = Vector3.Distance(mp, u.Position);
                if (d < bd) { bd = d; best = u; }
            }
            if (best == null) { Say(toTeacher ? "附近没有老师" : "附近没有学生"); return; }
            SnapTo(me, best.Position);
            Say("已传送 → " + (toTeacher ? "老师" : "学生") + " " + bd.ToString("0") + "米");
        }

        static void SnapTo(Unit me, Vector3 t)
        {
            try
            {
                var loc = me.GetComponent<LocomotionComponent>();
                if (loc != null) { loc.SetPosition(t + Vector3.up * 0.05f); return; }
            }
            catch { }
            try
            {
                var tr = me.transform;
                if (tr != null) tr.position = t + Vector3.up * 0.05f;
            }
            catch { }
        }

        static void Collect(UnitManager um)
        {
            _buf.Clear();
            if (um == null || um.unitsDict == null) return;
            try
            {
                foreach (var kv in um.unitsDict)
                {
                    var u = kv.Value;
                    if (u != null) _buf.Add(u);
                }
            }
            catch { }
        }

        void OnGUI()
        {
            var fnt = CnFont();
            if (_lbl == null)
            {
                _lbl = new GUIStyle(GUI.skin.label);
                _lbl.fontSize = 14;
                _lbl.normal.textColor = Color.white;
                if (fnt != null) _lbl.font = fnt;
                _big = new GUIStyle(_lbl); _big.fontSize = 17; _big.fontStyle = FontStyle.Bold;
                _small = new GUIStyle(_lbl); _small.fontSize = 13;
                _tag = new GUIStyle(_lbl); _tag.fontSize = 12;
            }
            else if (fnt != null && _lbl.font != fnt)
            {
                _lbl.font = fnt; _big.font = fnt; _small.font = fnt; _tag.font = fnt;
            }
            GUI.depth = -100;
            if (Esp) DrawEsp();
            Selector.Draw();     // 准星锁定圈
            Hud.Draw();          // v9.1: 左上角常驻「当前开启」清单
            if (Menu) DrawMenu();
            if (_msg.Length > 0 && Time.realtimeSinceStartup < _msgTill)
            {
                var sz = _big.CalcSize(new GUIContent(_msg));
                var r = new Rect(Screen.width / 2f - sz.x / 2f, Screen.height - 90, sz.x + 4, 26);
                Fill(new Rect(r.x - 8, r.y - 4, r.width + 16, 32), new Color(0f, 0f, 0f, 0.6f), 0f);
                var prev = _big.normal.textColor;
                _big.normal.textColor = new Color(0.35f, 1f, 0.75f);
                GUI.Label(r, _msg, _big);
                _big.normal.textColor = prev;
            }
        }

        void DrawEsp()
        {
            var um = UM();
            if (um == null) return;
            Camera cam = Camera.main;
            if (cam == null)
            {
                try { if (GameRoot.Inst != null) cam = GameRoot.Inst.tempCamera; } catch { }
            }
            if (cam == null) return;

            var me = um.localPlayer;
            Vector3 mp = me != null ? me.Position : Vector3.zero;
            Collect(um);

            float sw = Screen.width, sh = Screen.height;
            for (int i = 0; i < _buf.Count; i++)
            {
                var u = _buf[i];
                if (u == null || u == me) continue;
                int sub;
                try { sub = (int)u.UnitSubType; } catch { continue; }
                bool isT = sub == 101;
                bool isS = sub == 201;
                bool isN = sub == 202;
                if (!isT && !isS && !isN) continue;
                if (isN && !ShowNpc) continue;
                Vector3 p;
                try { p = u.Position; } catch { continue; }
                if (p.x == 0f && p.y == 0f && p.z == 0f) continue;
                float dist = Vector3.Distance(mp, p);
                if (dist > MaxDist) continue;

                Vector3 hd = cam.WorldToScreenPoint(p + Vector3.up * 1.75f);
                Vector3 ft = cam.WorldToScreenPoint(p);
                if (hd.z < 0.15f || ft.z < 0.15f) continue;
                float hx = hd.x, hy = sh - hd.y;
                float fx = ft.x, fy = sh - ft.y;
                float bh = Mathf.Abs(fy - hy);
                if (bh < 4f) bh = 4f;
                float bw = bh * 0.44f;

                Color c = isT ? new Color(1f, 0.22f, 0.22f, 1f)
                        : isS ? new Color(0.30f, 1f, 0.42f, 1f)
                              : new Color(0.65f, 0.65f, 0.65f, 1f);

                bool stun = false, sit = false, bed = false, dead = false;
                try
                {
                    dead = u.IsDead; stun = u.IsStun; sit = u.IsSitting; bed = u.IsOnBed;
                }
                catch { }

                if (dead) c = new Color(0.4f, 0.4f, 0.4f, 0.6f);

                if (Bones) DrawSkeleton(u, cam, c);

                // ---- v4.2：不再画实心方框（那是"染到方框上"的元凶），
                //      只画四个角括号，人物模型本身靠 Skin 的透视材质染色 ----
                if (Skin.Wall)
                {
                    Color sc = Skin.ColorOf(u);
                    bool blocked = Skin.Blocked(cam, u, p);
                    if (!dead)
                    {
                        sc.a = blocked ? 0.95f : 0.65f;
                        float cw = Mathf.Min(bw * 0.30f, 10f);
                        float ch2 = Mathf.Min(bh * 0.22f, 14f);
                        float x0 = hx - bw / 2f, x1 = hx + bw / 2f;
                        float y0 = hy, y1 = hy + bh;
                        Line(new Vector2(x0, y0), new Vector2(x0 + cw, y0), sc, 2f);
                        Line(new Vector2(x0, y0), new Vector2(x0, y0 + ch2), sc, 2f);
                        Line(new Vector2(x1, y0), new Vector2(x1 - cw, y0), sc, 2f);
                        Line(new Vector2(x1, y0), new Vector2(x1, y0 + ch2), sc, 2f);
                        Line(new Vector2(x0, y1), new Vector2(x0 + cw, y1), sc, 2f);
                        Line(new Vector2(x0, y1), new Vector2(x0, y1 - ch2), sc, 2f);
                        Line(new Vector2(x1, y1), new Vector2(x1 - cw, y1), sc, 2f);
                        Line(new Vector2(x1, y1), new Vector2(x1, y1 - ch2), sc, 2f);
                    }
                }

                Fill(new Rect(hx - bw / 2f, hy, bw, bh), c, 1.2f);
                Fill(new Rect(hx - 1.5f, hy - 3f, 3f, 3f), c, 1f);
                if (Tracers)
                    Line(new Vector2(sw / 2f, sh), new Vector2(hx, hy), new Color(c.r, c.g, c.b, 0.45f), 1f);

                string tag = isT ? "[师]" : (isS ? "[生]" : "[NPC]");
                string extra = "";
                if (stun) extra += " 晕";
                if (sit) extra += " 坐";
                if (bed) extra += " 床";
                if (dead) extra += " 死";
                string text = tag + " " + dist.ToString("0") + "米" + extra;
                var st = _tag;
                var sz = st.CalcSize(new GUIContent(text));
                var prev = st.normal.textColor;
                st.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
                GUI.Label(new Rect(hx - sz.x / 2f + 1f, hy - 19f + 1f, sz.x, 18f), text, st);
                st.normal.textColor = c;
                GUI.Label(new Rect(hx - sz.x / 2f, hy - 19f, sz.x, 18f), text, st);
                st.normal.textColor = prev;
            }
        }

        // ---------- 骨骼透视（v1.5 默认关，改由模型染色代替）----------
        public static bool Bones = true;    // v5.1: 默认开骨骼透视
        public static bool BoneDots = false; // 只要线段，不要关节点

        /// <summary>骨骼连线表：Child[i] -> Parent[i]，都取自 SkinnedMeshRenderer.bones</summary>
        sealed class Rig
        {
            public readonly List<Transform> Child = new List<Transform>();
            public readonly List<Transform> Parent = new List<Transform>();
            public readonly List<Transform> Joints = new List<Transform>();
            public bool valid;
        }

        static readonly Dictionary<ulong, Rig> _rigs = new Dictionary<ulong, Rig>();
        static readonly Dictionary<ulong, Animator> _anims = new Dictionary<ulong, Animator>();

        // 这些骨骼画出来只会变成毛刺，直接跳过
        static readonly string[] BONE_SKIP = new string[]
        {
            "finger", "thumb", "index", "middle", "ring", "pinky", "toe",
            "eye", "jaw", "tongue", "breast", "twist", "hair", "ear", "nose", "teeth"
        };

        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant();
            return s.Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "")
                    .Replace("mixamorig:", "").Replace("bip01", "").Replace("bip001", "");
        }

        static Transform RootOf(Unit u)
        {
            Transform root = null;
            try { if (u.model != null) root = u.model.transform; } catch { }
            if (root == null) { try { root = u.transform; } catch { } }
            return root;
        }

        static Rig BuildRig(Transform root)
        {
            var rig = new Rig();
            if (root == null) return rig;

            var set = new HashSet<Transform>();
            try
            {
                var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < smrs.Length; i++)
                {
                    var bo = smrs[i].bones;
                    if (bo == null) continue;
                    for (int k = 0; k < bo.Length; k++) if (bo[k] != null) set.Add(bo[k]);
                }
            }
            catch { }
            if (set.Count < 4) return rig;

            foreach (var t in set)
            {
                string n = Norm(t.name);
                bool skip = false;
                for (int i = 0; i < BONE_SKIP.Length; i++)
                    if (n.IndexOf(BONE_SKIP[i], StringComparison.Ordinal) >= 0) { skip = true; break; }
                if (skip) continue;

                rig.Joints.Add(t);
                Transform p = t.parent;
                if (p != null && set.Contains(p))
                {
                    rig.Child.Add(t);
                    rig.Parent.Add(p);
                }
            }
            rig.valid = rig.Joints.Count >= 4;
            return rig;
        }

        static Rig GetRig(Unit u)
        {
            if (u == null) return null;
            Rig r;
            ulong uid;
            try { uid = u.UID; } catch { return null; }
            if (_rigs.TryGetValue(uid, out r) && r != null && r.valid) return r;

            Transform root = RootOf(u);
            if (root == null) return null;
            r = BuildRig(root);
            try { _rigs[uid] = r; } catch { }
            return r.valid ? r : null;
        }

        static Animator GetAnim(Unit u)
        {
            if (u == null) return null;
            Animator a = null;
            ulong uid;
            try { uid = u.UID; } catch { return null; }
            try
            {
                if (_anims.TryGetValue(uid, out a) && a != null) return a;
            }
            catch { }
            Transform root = RootOf(u);
            if (root == null) return null;
            try { a = root.GetComponentInChildren<Animator>(true); } catch { }
            try { _anims[uid] = a; } catch { }
            return a;
        }

        static readonly HumanBodyBones[] BK = new HumanBodyBones[]
        {
            HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Chest, HumanBodyBones.Hips,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        static readonly int[] BL = new int[]
        {
            // 三角洲风格：脊(颈-胸-胯) + 左臂 + 右臂 + 左腿 + 右腿
            1,2, 2,3,
            2,4, 4,5,
            2,7, 7,8,
            3,10, 10,11,
            3,13, 13,14
        };

        /// <summary>骨骼 ESP：Humanoid → 骨骼网格父子连线 → 挂点，三级回退</summary>
        void DrawSkeleton(Unit u, Camera cam, Color c)
        {
            Animator a = GetAnim(u);
            if (a != null)
            {
                bool human = false;
                try { human = a.isHuman; } catch { }
                if (human && DrawHumanoid(a, cam, c)) return;
            }
            Rig r = GetRig(u);
            if (r != null && DrawRig(r, cam, c)) return;
            DrawParts(u, cam, c);
        }

        bool DrawHumanoid(Animator a, Camera cam, Color c)
        {
            float sh = Screen.height;
            var pts = new Vector2[BK.Length];
            var ok = new bool[BK.Length];
            int n = 0;
            for (int i = 0; i < BK.Length; i++)
            {
                Transform t = null;
                try { t = a.GetBoneTransform(BK[i]); } catch { }
                if (t == null) continue;
                Vector3 sp;
                try { sp = cam.WorldToScreenPoint(t.position); } catch { continue; }
                if (sp.z < 0.15f || float.IsNaN(sp.x)) continue;
                pts[i] = new Vector2(sp.x, sh - sp.y);
                ok[i] = true;
                n++;
            }
            if (n < 4) return false;

            Color lc = new Color(c.r, c.g, c.b, 0.95f);
            for (int i = 0; i + 1 < BL.Length; i += 2)
            {
                int x = BL[i], y = BL[i + 1];
                if (!ok[x] || !ok[y]) continue;
                Line(pts[x], pts[y], lc, 2.1f);
            }
            if (BoneDots)
            {
                for (int i = 0; i < pts.Length; i++)
                {
                    if (!ok[i]) continue;
                    Fill(new Rect(pts[i].x - 1.5f, pts[i].y - 1.5f, 3f, 3f), lc, 1f);
                }
            }
            return true;
        }

        bool DrawRig(Rig r, Camera cam, Color c)
        {
            Color lc = new Color(c.r, c.g, c.b, 0.95f);
            int drawn = 0;
            for (int i = 0; i < r.Child.Count; i++)
            {
                var pa = P2(cam, r.Child[i]);
                if (!pa.HasValue) continue;
                var pb = P2(cam, r.Parent[i]);
                if (!pb.HasValue) continue;
                Line(pa.Value, pb.Value, lc, 1.6f);
                drawn++;
            }
            if (BoneDots)
            {
                for (int i = 0; i < r.Joints.Count; i++)
                {
                    var p = P2(cam, r.Joints[i]);
                    if (!p.HasValue) continue;
                    Fill(new Rect(p.Value.x - 1.5f, p.Value.y - 1.5f, 3f, 3f), lc, 1f);
                }
            }
            return drawn > 0;
        }

        int Lnk(Camera cam, Transform a, Transform b, Color c)
        {
            var pa = P2(cam, a); var pb = P2(cam, b);
            if (!pa.HasValue || !pb.HasValue) return 0;
            Line(pa.Value, pb.Value, c, 1.7f);
            return 1;
        }

        /// <summary>没有蒙皮骨骼时，用 ObjectModel.Parts 的挂点画简化骨架</summary>
        void DrawParts(Unit u, Camera cam, Color c)
        {
            ObjectModel m = null;
            try { m = u.model; } catch { }
            if (m == null) return;
            Dictionary<EUnitPartType, Transform> parts = null;
            try { parts = m.Parts; } catch { }
            if (parts == null) return;

            Transform head = null, body = null, hl = null, hr = null, hd = null;
            try
            {
                parts.TryGetValue(EUnitPartType.HeadPosition, out head);
                parts.TryGetValue(EUnitPartType.HitBox_Body, out body);
                parts.TryGetValue(EUnitPartType.HandPosition_L, out hl);
                parts.TryGetValue(EUnitPartType.HandPosition, out hr);
                parts.TryGetValue(EUnitPartType.HitBox_Head, out hd);
            }
            catch { }
            if (head == null && body == null) return;

            Color lc = new Color(c.r, c.g, c.b, 0.95f);
            Lnk(cam, head, body, lc);
            Lnk(cam, body, hl, lc);
            Lnk(cam, body, hr, lc);
            Lnk(cam, head, hd, lc);
        }

        static Vector2? P2(Camera cam, Transform t)
        {
            if (t == null) return null;
            Vector3 sp;
            try { sp = cam.WorldToScreenPoint(t.position); } catch { return null; }
            if (sp.z < 0.15f || float.IsNaN(sp.x)) return null;
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        static string OnOff(bool b) { return b ? "开" : "关"; }

        /// <summary>F12：把场上每个单位的模型结构（骨骼数/骨骼名/是否 humanoid）写进 Player.log</summary>
        void DumpModels()
        {
            var um = UM();
            if (um == null) { Say("未进对局，没有单位可 dump"); return; }
            Collect(um);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[WceHack] ===== MODEL DUMP BEGIN =====");
            int cnt = 0;
            for (int i = 0; i < _buf.Count; i++)
            {
                var u = _buf[i];
                if (u == null) continue;
                if (cnt++ >= 8) break;
                ulong uid = 0; int sub = 0;
                try { uid = u.UID; } catch { }
                try { sub = (int)u.UnitSubType; } catch { }
                Transform root = RootOf(u);
                sb.Append("[WceHack] unit uid=").Append(uid).Append(" sub=").Append(sub)
                  .Append(" root=").Append(root == null ? "null" : root.name).AppendLine();
                if (root == null) continue;
                try
                {
                    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    sb.Append("[WceHack]   skinnedRenderers=").Append(smrs.Length).AppendLine();
                    for (int k = 0; k < smrs.Length && k < 4; k++)
                    {
                        var bo = smrs[k].bones;
                        sb.Append("[WceHack]   smr[").Append(k).Append("] ").Append(smrs[k].name)
                          .Append(" bones=").Append(bo == null ? 0 : bo.Length).AppendLine();
                        if (bo == null) continue;
                        var nb = new System.Text.StringBuilder();
                        for (int z = 0; z < bo.Length && z < 32; z++)
                        {
                            nb.Append(bo[z] == null ? "null" : bo[z].name).Append(',');
                        }
                        sb.Append("[WceHack]     ").Append(nb).AppendLine();
                    }
                    var anim = GetAnim(u);
                    string kind = "null";
                    if (anim != null) { bool hu = false; try { hu = anim.isHuman; } catch { } kind = hu ? "humanoid" : "generic"; }
                    sb.Append("[WceHack]   animator=").Append(kind).AppendLine();
                    var all = root.GetComponentsInChildren<Transform>(true);
                    sb.Append("[WceHack]   transforms=").Append(all.Length).AppendLine();
                    var nm = new System.Text.StringBuilder();
                    for (int z = 0; z < all.Length && z < 50; z++) nm.Append(all[z].name).Append(',');
                    sb.Append("[WceHack]   names: ").Append(nm).AppendLine();
                    Rig rr = GetRig(u);
                    sb.Append("[WceHack]   rig=").Append(rr == null ? "none" : ("joints=" + rr.Joints.Count + " links=" + rr.Child.Count)).AppendLine();
                }
                catch (Exception e) { sb.Append("[WceHack]   ERR ").Append(e.Message).AppendLine(); }
            }
            sb.AppendLine("[WceHack] ===== MODEL DUMP END =====");
            try { Debug.Log(sb.ToString()); } catch { }
            Say("模型结构已写入 Player.log");
        }

        public static void Teleport(bool toTeacher) { DoTeleport(toTeacher); }

        public static void DoPanic()
        {
            try
            {
                Escape.Auto = true;
                string rs = Escape.DoEscapeOnce();
                Unit m2 = null;
                try { UnitManager u0 = UM(); if (u0 != null) m2 = u0.localPlayer; } catch { }
                string ps = "";
                if (m2 != null) { Pillar.Do(m2); ps = " 柱子:" + Pillar.Last; }
                Say("脱困：" + rs + ps);
                HackPlugin.Log("[WceHack] 手动脱困 " + rs);
            }
            catch (Exception e) { Say("脱困失败 " + e.Message); }
        }

        void DrawMenu()
        {
            UiKit.Draw();
        }

        static void Fill(Rect r, Color c, float border)
        {
            var prev = GUI.color;
            if (border <= 0f)
            {
                GUI.color = c;
                GUI.DrawTexture(r, White());
            }
            else
            {
                GUI.color = c;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, border), White());
                GUI.DrawTexture(new Rect(r.x, r.yMax - border, r.width, border), White());
                GUI.DrawTexture(new Rect(r.x, r.y, border, r.height), White());
                GUI.DrawTexture(new Rect(r.xMax - border, r.y, border, r.height), White());
            }
            GUI.color = prev;
        }

        // 透墙用的实心填充
        static void Solid(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White());
            GUI.color = prev;
        }

        static void Line(Vector2 a, Vector2 b, Color c, float w)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(a.x, a.y - w / 2f, len, w), White());
            GUI.color = prev;
            GUI.matrix = m;
        }
    }
}
