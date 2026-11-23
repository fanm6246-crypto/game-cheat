    // ============================================================
    //  v7.0  UiKit —— 高级悬浮菜单（玻璃拟态 / 圆角 / 动画 / 可点击）
    // ============================================================
    public static class UiKit
    {
        // ---- 配色 ----
        public static Color CAcc = new Color(0.306f, 0.878f, 0.702f, 1f);
        public static Color CDanger = new Color(1f, 0.38f, 0.40f, 1f);
        public static Color CText = new Color(0.933f, 0.953f, 0.973f, 1f);
        public static Color CSub = new Color(0.541f, 0.588f, 0.647f, 1f);
        public static Color CLine = new Color(0.153f, 0.184f, 0.227f, 1f);

        public static bool Open = false;
        public static int Cat = 0;

        static Texture2D _tPanel, _tCard, _tCardOn, _tPillOn, _tPillOff, _tDot, _tBtn, _tBtnHi, _tCat, _tCatOn, _tGlow;
        static GUIStyle _sPanel, _sCard, _sCardOn, _sPillOn, _sPillOff, _sDot, _sBtn, _sBtnHi, _sCat, _sCatOn, _sGlow;
        static GUIStyle _stTitle, _stSub, _stRow, _stRowOn, _stKey, _stCatOn, _stCatOff, _stFoot, _stClose;
        static Font _font;
        static bool _built;
        static float _openAt = -99f;
        static string _tip = "";

        public static readonly string[] Cats = new string[] { "视觉", "移动", "战斗", "互动", "防护" };

        // ---------- 圆角贴图 ----------
        static Texture2D RoundTex(int size, int r, Color fill, Color border, float bw)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.ARGB32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = 0f, dy = 0f;
                    if (x < r) dx = r - x; else if (x >= size - r) dx = x - (size - r - 1);
                    if (y < r) dy = r - y; else if (y >= size - r) dy = y - (size - r - 1);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c;
                    if (d > r) c = new Color(0f, 0f, 0f, 0f);
                    else if (d > r - bw) c = border;
                    else c = fill;
                    t.SetPixel(x, y, c);
                }
            }
            t.Apply();
            return t;
        }

        static GUIStyle StyleOf(Texture2D tex, int border)
        {
            GUIStyle s = new GUIStyle();
            s.normal.background = tex;
            s.border = new RectOffset(border, border, border, border);
            s.padding = new RectOffset(0, 0, 0, 0);
            s.margin = new RectOffset(0, 0, 0, 0);
            s.stretchWidth = true;
            s.stretchHeight = true;
            return s;
        }

        static Font F()
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
            if (F() != null) s.font = F();
            return s;
        }

        static void Build()
        {
            if (_built) return;
            _built = true;

            _tPanel = RoundTex(48, 16, new Color(0.043f, 0.051f, 0.071f, 0.975f), new Color(0.22f, 0.26f, 0.32f, 0.85f), 1.3f);
            _tGlow = RoundTex(48, 16, new Color(0f, 0f, 0f, 0f), new Color(0.306f, 0.878f, 0.702f, 0.14f), 2.5f);
            _tCard = RoundTex(26, 9, new Color(0.078f, 0.094f, 0.125f, 1f), new Color(0.133f, 0.157f, 0.196f, 0.75f), 1f);
            _tCardOn = RoundTex(26, 9, new Color(0.071f, 0.129f, 0.114f, 1f), new Color(0.306f, 0.878f, 0.702f, 0.65f), 1.2f);
            _tCat = RoundTex(22, 8, new Color(0.078f, 0.094f, 0.125f, 0.55f), new Color(0f, 0f, 0f, 0f), 0f);
            _tCatOn = RoundTex(22, 8, new Color(0.094f, 0.161f, 0.141f, 1f), new Color(0.306f, 0.878f, 0.702f, 0.55f), 1.1f);
            _tPillOn = RoundTex(28, 14, CAcc, CAcc, 0f);
            _tPillOff = RoundTex(28, 14, new Color(0.196f, 0.224f, 0.271f, 1f), new Color(0.24f, 0.27f, 0.32f, 1f), 1f);
            _tDot = RoundTex(18, 9, Color.white, Color.white, 0f);
            _tBtn = RoundTex(22, 11, new Color(0.106f, 0.129f, 0.169f, 1f), new Color(0.184f, 0.212f, 0.259f, 1f), 1f);
            _tBtnHi = RoundTex(22, 11, new Color(0.196f, 0.545f, 0.443f, 1f), new Color(0.306f, 0.878f, 0.702f, 1f), 1.2f);

            _sPanel = StyleOf(_tPanel, 16);
            _sGlow = StyleOf(_tGlow, 16);
            _sCard = StyleOf(_tCard, 9);
            _sCardOn = StyleOf(_tCardOn, 9);
            _sCat = StyleOf(_tCat, 8);
            _sCatOn = StyleOf(_tCatOn, 8);
            _sPillOn = StyleOf(_tPillOn, 14);
            _sPillOff = StyleOf(_tPillOff, 14);
            _sDot = StyleOf(_tDot, 9);
            _sBtn = StyleOf(_tBtn, 11);
            _sBtnHi = StyleOf(_tBtnHi, 11);

            _stTitle = Mk(22, CText); _stTitle.fontStyle = FontStyle.Bold;
            _stSub = Mk(12, CSub);
            _stRow = Mk(15, CText);
            _stRowOn = Mk(15, new Color(0.78f, 1f, 0.92f, 1f));
            _stKey = Mk(12, CSub); _stKey.alignment = TextAnchor.MiddleRight;
            _stCatOn = Mk(15, CAcc); _stCatOn.fontStyle = FontStyle.Bold; _stCatOn.alignment = TextAnchor.MiddleLeft;
            _stCatOff = Mk(15, CSub); _stCatOff.alignment = TextAnchor.MiddleLeft;
            _stFoot = Mk(12, CSub);
            _stClose = Mk(17, CSub); _stClose.alignment = TextAnchor.MiddleCenter;
        }

        // ---------- 基础绘制 ----------
        public static void Rect(Rect r, GUIStyle s) { GUI.Box(r, GUIContent.none, s); }

        public static void RectC(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static Vector2 Mp() { return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y); }

        // ---------- 行 ----------
        static void RowToggle(Rect r, string name, string key, bool on, string tip)
        {
            bool hov = r.Contains(Mp());
            Rect(r, on ? _sCardOn : _sCard);
            if (on) RectC(new Rect(r.x, r.y + 10f, 3f, r.height - 20f), CAcc);
            if (hov && !on) RectC(new Rect(r.x, r.y + 1f, r.width, 1f), new Color(0.306f, 0.878f, 0.702f, 0.22f));

            GUI.Label(new Rect(r.x + 20f, r.y, r.width - 180f, r.height), name, on ? _stRowOn : _stRow);
            if (key != null && key.Length > 0)
                GUI.Label(new Rect(r.x + r.width - 128f, r.y, 62f, r.height), key, _stKey);

            float pw = 48f, ph = 24f;
            Rect pill = new Rect(r.x + r.width - 66f, r.y + (r.height - ph) / 2f, pw, ph);
            Rect(pill, on ? _sPillOn : _sPillOff);
            float dx = on ? pill.x + pw - ph + 4f : pill.x + 4f;
            Color dc = on ? new Color(0.04f, 0.10f, 0.08f, 1f) : new Color(0.55f, 0.58f, 0.62f, 1f);
            RectC(new Rect(dx, pill.y + 5f, ph - 10f, ph - 10f), dc);
            Rect(new Rect(dx - 1f, pill.y + 4f, ph - 8f, ph - 8f), _sDot);

            if (hov)
            {
                _tip = tip;
                if (Input.GetMouseButtonDown(0)) ClickToggle(name);
            }
        }

        static void RowButton(Rect r, string name, string key)
        {
            bool hov = r.Contains(Mp());
            Rect(r, hov ? _sBtnHi : _sBtn);
            GUI.Label(new Rect(r.x + 20f, r.y, r.width - 140f, r.height), name, hov ? _stRowOn : _stRow);
            if (key != null && key.Length > 0)
                GUI.Label(new Rect(r.x + r.width - 128f, r.y, 96f, r.height), key, _stKey);
            if (hov && Input.GetMouseButtonDown(0)) ClickButton(name);
        }

        static void RowSlider(Rect r, string name, float val, float min, float max)
        {
            Rect(r, _sCard);
            GUI.Label(new Rect(r.x + 20f, r.y + 3f, 240f, 22f), name, _stRow);
            GUI.Label(new Rect(r.x + r.width - 128f, r.y + 3f, 96f, 22f), val.ToString("0"), _stKey);

            Rect bar = new Rect(r.x + 20f, r.y + 30f, r.width - 40f, 5f);
            RectC(bar, new Color(0.145f, 0.169f, 0.204f, 1f));
            float t = Mathf.Clamp01((val - min) / Mathf.Max(0.001f, max - min));
            RectC(new Rect(bar.x, bar.y, bar.width * t, bar.height), CAcc);
            RectC(new Rect(bar.x + bar.width * t - 5f, bar.y - 5f, 10f, 15f), Color.white);

            if (bar.Contains(Mp()) && Input.GetMouseButton(0))
                ClickSlider(name, Mathf.Lerp(min, max, Mathf.Clamp01((Mp().x - bar.x) / bar.width)));
        }

        // ---------- 交互 ----------
        static void ClickToggle(string name)
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
            else if (name == "散射(霰弹)") { Shot.Scatter = !Shot.Scatter; }
            else if (name == "速射 + 连发") { Shot.Rapid = !Shot.Rapid; Shot.AutoFire = Shot.Rapid; }
            else if (name == "攻击穿墙") { WallHack.On = !WallHack.On; }
            else if (name == "秒完成互动") { Instant.On = !Instant.On; }
            else if (name == "无限物品") { Instant.InfItems = !Instant.InfItems; }
            else if (name == "无限投掷") { Stock.On = !Stock.On; if (Stock.On) Stock.ClearCooldown(); }
            else if (name == "不可被抓") { Protect.NoCatch = !Protect.NoCatch; }
            else if (name == "不可被打晕") { Protect.NoStun = !Protect.NoStun; }
            else if (name == "完全隐形") { Ghost.On = !Ghost.On; if (!Ghost.On) Ghost.Reset(); }
            else if (name == "自动刷新状态") { Refresh.On = !Refresh.On; }
            else if (name == "视角锁定") { StableCam.On = !StableCam.On; }
            else if (name == "防飞出地图") { Safe.On = !Safe.On; }
        }

        static void ClickButton(string name)
        {
            if (name == "秒解救同伴") Rescue.Do();
            else if (name == "立即脱困") HackBehaviour.DoPanic();
            else if (name == "传送到老师") HackBehaviour.Teleport(true);
            else if (name == "传送到学生") HackBehaviour.Teleport(false);
            else if (name == "刷新状态一次") Refresh.Do();
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

            float k = Mathf.Clamp01((Time.unscaledTime - _openAt) / 0.16f);
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            float W = 880f, H = 648f;
            float X = (Screen.width - W) / 2f;
            float Y = (Screen.height - H) / 2f + (1f - ease) * 14f;

            Color pc = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, ease);

            RectC(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.42f * ease));

            Rect(X - 6, Y - 6, W + 12, H + 12, _sGlow);
            Rect(new Rect(X, Y, W, H), _sPanel);
            RectC(new Rect(X + 22, Y, W - 44, 1.6f), new Color(0.306f, 0.878f, 0.702f, 0.85f));

            // 标题
            RectC(new Rect(X + 26, Y + 26, 4f, 30f), CAcc);
            GUI.Label(new Rect(X + 40, Y + 18, 460, 30), "WORST CLASS EVER", _stTitle);
            GUI.Label(new Rect(X + 42, Y + 48, 460, 18), "external helper   ·   v7.0   ·   Ins 开关", _stSub);

            Rect close = new Rect(X + W - 50, Y + 22, 28, 28);
            bool chov = close.Contains(Mp());
            if (chov) RectC(close, new Color(0.36f, 0.13f, 0.16f, 1f));
            GUIStyle cs = chov ? Mk(17, CDanger) : _stClose;
            cs.alignment = TextAnchor.MiddleCenter;
            GUI.Label(close, "×", cs);
            if (chov && Input.GetMouseButtonDown(0)) { Open = false; HackBehaviour.Menu = false; }

            RectC(new Rect(X + 20, Y + 78, W - 40, 1f), CLine);

            // 左栏
            float catX = X + 24f, catY = Y + 94f;
            for (int i = 0; i < Cats.Length; i++)
            {
                Rect cr = new Rect(catX, catY + i * 50f, 152f, 40f);
                bool sel = (i == Cat);
                bool hov = cr.Contains(Mp());
                if (sel) Rect(cr, _sCatOn);
                else if (hov) Rect(cr, _sCat);
                if (sel) RectC(new Rect(cr.x, cr.y + 10f, 3f, 20f), CAcc);
                GUI.Label(new Rect(cr.x + 22f, cr.y, 126f, 40f), Cats[i], sel ? _stCatOn : _stCatOff);
                if (hov && Input.GetMouseButtonDown(0)) Cat = i;
            }

            float fx = X + 26f, fy = Y + H - 108f;
            RectC(new Rect(fx, fy - 14, 150f, 1f), CLine);
            Unit m = null;
            try { m = UnitManager.Instance != null ? UnitManager.Instance.localPlayer : null; } catch { }
            string role = m == null ? "未进对局" : (m.IsTeacher ? "老师" : "学生");
            GUI.Label(new Rect(fx, fy, 150, 16), "身份", _stFoot);
            GUI.Label(new Rect(fx, fy + 15, 150, 22), role, _stRow);

            int nt = 0, ns = 0, nn = 0;
            try
            {
                UnitManager um = UnitManager.Instance;
                if (um != null && um.unitsDict != null)
                {
                    foreach (var kv in um.unitsDict)
                    {
                        Unit u = kv.Value; if (u == null) continue;
                        int sub = (int)u.UnitSubType;
                        if (sub == 101) nt++; else if (sub == 201) ns++; else if (sub == 202) nn++;
                    }
                }
            }
            catch { }
            GUI.Label(new Rect(fx, fy + 44, 150, 16), "场上", _stFoot);
            GUI.Label(new Rect(fx, fy + 59, 150, 22), nt + " / " + ns + " / " + nn, _stRow);

            // 右栏
            float rx = X + 196f, ry = Y + 96f;
            float rw = W - 224f;
            _tip = "";

            if (Cat == 0)
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, 46), "透视 ESP", "F1", HackBehaviour.Esp, "画出所有老师 / 学生 / NPC 的位置"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "骨骼透视", "F11", HackBehaviour.Bones, "只画躯干 + 两臂 + 两腿"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "模型染色", "—", Skin.On, "学生绿 / 老师红，整个模型换色"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "染色透墙", "End", Skin.Wall, "被墙挡住也完整绘制"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "显示 NPC", "F10", HackBehaviour.ShowNpc, "把 NPC 学生也一并画出来"); y += 53;
                RowSlider(new Rect(rx, y + 4, rw, 50), "距离上限", HackBehaviour.MaxDist, 50f, 400f);
            }
            else if (Cat == 1)
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, 46), "加速 x2.5", "F8", HackPatches.Speed, "本地移动速度 × 2.5"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "无限冲刺", "F7", HackPatches.InfSprint, "冲刺不消耗体力"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "无限体力", "Home", Energy.On, "技能能量 9999 / 疲劳归零"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "御剑飞行", "PgUp", Fly.On, "WASD 平移 · 空格上升 · Ctrl 下降"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "第三人称", "—", ThirdPerson.On, "把主相机往后拉，看自己的背面"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "自动旋转", "—", AutoSpin.On, "自己原地匀速转圈"); y += 53;
                RowSlider(new Rect(rx, y + 2, rw, 50), "第三人称距离", ThirdPerson.Distance, 1.5f, 8f);
            }
            else if (Cat == 2)
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, 46), "散射(霰弹)", "F3", Shot.Scatter, "一次扔出 7 颗粉笔头"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "速射 + 连发", "F4", Shot.Rapid, "无冷却，按住左键 20 发/秒"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "攻击穿墙", "1", WallHack.On, "粉笔 / 黑板擦 / 剑气无视墙体"); y += 53;
                RowButton(new Rect(rx, y + 6, rw, 44), "传送到老师", "F5"); y += 56;
                RowButton(new Rect(rx, y, rw, 44), "传送到学生", "F6");
            }
            else if (Cat == 3)
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, 46), "秒完成互动", "9", Instant.On, "写板书 / 吃零食 / 吹笛 全部瞬发"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "无限物品", "0", Instant.InfItems, "手上与嘴里的物品不消耗"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "无限投掷", "PgDn", Stock.On, "烟雾 / 闪光 不扣补货冷却"); y += 53;
                RowButton(new Rect(rx, y + 6, rw, 44), "秒解救同伴", "2"); y += 56;
                RowButton(new Rect(rx, y, rw, 44), "立即脱困", "柱子 / 被抓");
            }
            else
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, 46), "不可被抓", "3", Protect.NoCatch, "老师的抓取判定直接失效"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "不可被打晕", "4", Protect.NoStun, "眩晕状态与屏幕特效都不进来"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "完全隐形", "Del", Ghost.On, "模型 + 名字 + 剪影全部消失"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "自动刷新状态", "R", Refresh.On, "每 0.08s 清掉被抓 / 眩晕 / 挂载"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "视角锁定", "5", StableCam.On, "眩晕不抖镜、不拉扯视角"); y += 53;
                RowToggle(new Rect(rx, y, rw, 46), "防飞出地图", "—", Safe.On, "Y 越界自动拉回地面"); y += 53;
                RowButton(new Rect(rx, y + 2, rw, 44), "刷新状态一次", "");
            }

            // 底部
            float by = Y + H - 40f;
            RectC(new Rect(X + 20, by - 10, W - 40, 1f), CLine);
            GUI.Label(new Rect(X + 26, by, 520, 18), _tip.Length > 0 ? _tip : "点击行切换开关   ·   Ins 关闭菜单", _stFoot);
            GUI.Label(new Rect(X + W - 200, by, 174, 18),
                "FPS " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)), _stKey);

            GUI.color = pc;
        }

        public static void Toggle()
        {
            Open = !Open;
            HackBehaviour.Menu = Open;
            if (Open) { _openAt = Time.unscaledTime; _built = _built; }
        }
    }
