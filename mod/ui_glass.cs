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
            float a = (Target - Value) * 220f - Vel * 20f;
            Vel += a * dt;
            Value += Vel * dt;
            if (Mathf.Abs(Target - Value) < 0.0008f && Mathf.Abs(Vel) < 0.0008f) { Value = Target; Vel = 0f; }
        }
    }

    public static class Glass
    {
        public static bool On = true;
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
                if (_blur != null) Object.Destroy(_blur);
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
                Object.Destroy(src);
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
        public static bool On = true;
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
        public static Color CInk = new Color(0.949f, 0.961f, 0.976f, 1f);
        public static Color CInk2 = new Color(1f, 1f, 1f, 0.62f);
        public static Color CInk3 = new Color(1f, 1f, 1f, 0.38f);

        public static bool Open = false;
        public static int Cat = 0;

        // v8.1：只保留一个可自定义的菜单键，其余热键全部取消
        public static KeyCode MenuKey = KeyCode.Insert;
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

        const float W = 940f, H = 676f;
        const float R_OUT = 32f, R_CARD = 22f;

        // ---- 贴图 ----
        static Texture2D _texShadow, _texPanel, _texRim, _texCard, _texCardOn,
                          _texCat, _texCatOn, _texPillOn, _texPillOff, _texKnob,
                          _texSpot, _texGrad, _texWhite;
        static GUIStyle _sShadow, _sPanel, _sCard, _sCardOn, _sCat, _sCatOn, _sPillOn, _sPillOff;

        static GUIStyle _stTitle, _stSub, _stRow, _stTip, _stKey, _stCatOn, _stCatOff, _stFoot, _stVal, _stClose;
        static Font _font;
        static bool _built;

        static Spring _anim = new Spring(0f);
        static Spring _hover = new Spring(0f);
        static float _openAt = -99f;
        static string _tip = "";
        static Rect _panelRect;
        static Vector2 _mouse;

        public static readonly string[] Cats = new string[] { "视觉", "移动", "战斗", "互动", "防护" };

        // ---------- 圆角贴图（上下不同描边色 = 物理 Rim Light）----------
        static Texture2D Round(int size, int r, Color fill, Color rimTop, Color rimBottom, float bw)
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
            s.clipping = TextClipping.Clip;
            if (F() != null) s.font = F();
            return s;
        }

        static void Build()
        {
            if (_built) return;
            _built = true;

            Color ink = new Color(0.035f, 0.031f, 0.047f, 0.90f);
            Color rimT = new Color(1f, 1f, 1f, 0.62f);
            Color rimB = new Color(0f, 0f, 0f, 0.34f);

            _texShadow = Round(64, 22, new Color(0f, 0f, 0f, 0.42f), new Color(0f, 0f, 0f, 0.42f), new Color(0f, 0f, 0f, 0.42f), 0f);
            _texPanel = Round(72, 26, ink, rimT, rimB, 1.6f);
            _texRim = Round(72, 26, new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0.04f), 1.6f);

            _texCard = Round(30, 11, new Color(1f, 1f, 1f, 0.055f), new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.03f), 1f);
            _texCardOn = Round(30, 11, new Color(0.373f, 0.941f, 0.784f, 0.10f), new Color(0.373f, 0.941f, 0.784f, 0.42f), new Color(0.373f, 0.941f, 0.784f, 0.10f), 1.1f);
            _texCat = Round(24, 12, new Color(1f, 1f, 1f, 0.055f), new Color(1f, 1f, 1f, 0.10f), new Color(1f, 1f, 1f, 0.02f), 1f);
            _texCatOn = Round(24, 12, new Color(0.373f, 0.941f, 0.784f, 0.95f), new Color(1f, 1f, 1f, 0.90f), new Color(0.133f, 0.827f, 0.933f, 1f), 1.2f);
            _texPillOn = Round(32, 16, new Color(0.25f, 0.90f, 0.80f, 1f), new Color(1f, 1f, 1f, 0.75f), new Color(0.133f, 0.827f, 0.933f, 1f), 1.2f);
            _texPillOff = Round(32, 16, new Color(1f, 1f, 1f, 0.10f), new Color(1f, 1f, 1f, 0.16f), new Color(0f, 0f, 0f, 0.28f), 1f);
            _texKnob = Round(32, 16, new Color(0.98f, 0.99f, 1f, 1f), new Color(1f, 1f, 1f, 1f), new Color(0.78f, 0.83f, 0.89f, 1f), 1f);

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

            // 玻璃竖直渐变（顶部 18% 白 → 中部 3% → 底部 8%）
            int g = 64;
            _texGrad = new Texture2D(4, g, TextureFormat.ARGB32, false);
            _texGrad.hideFlags = HideFlags.HideAndDontSave;
            _texGrad.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < g; y++)
            {
                float t = (float)y / (g - 1);            // 0 底 -> 1 顶
                float a = t > 0.5f ? Mathf.Lerp(0.03f, 0.18f, (t - 0.5f) * 2f)
                                   : Mathf.Lerp(0.08f, 0.03f, t * 2f);
                for (int x = 0; x < 4; x++) _texGrad.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            _texGrad.Apply();

            _texWhite = Texture2D.whiteTexture;

            _sShadow = St(_texShadow, 22);
            _sPanel = St(_texPanel, 26);
            _sCard = St(_texCard, 11);
            _sCardOn = St(_texCardOn, 11);
            _sCat = St(_texCat, 12);
            _sCatOn = St(_texCatOn, 12);
            _sPillOn = St(_texPillOn, 16);
            _sPillOff = St(_texPillOff, 16);

            _stTitle = Mk(20, CInk); _stTitle.fontStyle = FontStyle.Bold;
            _stSub = Mk(12, CInk3);
            _stRow = Mk(15, CInk);
            _stTip = Mk(11, CInk3);
            _stKey = Mk(11, CInk3); _stKey.alignment = TextAnchor.MiddleRight;
            _stCatOn = Mk(14, new Color(0.024f, 0.137f, 0.110f, 1f)); _stCatOn.fontStyle = FontStyle.Bold; _stCatOn.alignment = TextAnchor.MiddleCenter;
            _stCatOff = Mk(14, CInk2); _stCatOff.alignment = TextAnchor.MiddleCenter;
            _stFoot = Mk(11, CInk3);
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
            Box(r, on ? _sCardOn : _sCard);
            if (hov)
            {
                Fill(new Rect(r.x + 14f, r.y + 1f, r.width - 28f, 1f), new Color(1f, 1f, 1f, 0.14f));
            }

            GUI.Label(new Rect(r.x + 18f, r.y + 8f, r.width - 170f, 20f), name, _stRow);
            GUI.Label(new Rect(r.x + 18f, r.y + 27f, r.width - 170f, 16f), tip, _stTip);

            if (key != null && key.Length > 0)
            {
                float kw = 12f + key.Length * 7f;
                Rect kr = new Rect(r.x + r.width - 86f - kw, r.y + (r.height - 22f) * 0.5f, kw, 22f);
                Box(kr, _sCat);
                GUI.Label(kr, key, _stKey);
            }

            float pw = 52f, ph = 30f;
            Rect pill = new Rect(r.x + r.width - 70f, r.y + (r.height - ph) * 0.5f, pw, ph);
            Box(pill, on ? _sPillOn : _sPillOff);
            float kx = on ? pill.x + pw - 27f : pill.x + 3f;
            Tex(new Rect(kx, pill.y + 3f, 24f, 24f), _texKnob, on ? new Color(0.05f, 0.20f, 0.17f, 1f) : Color.white);

            if (hov)
            {
                _tip = tip;
                if (Input.GetMouseButtonDown(0)) ClickToggle(name);
            }
        }

        static void RowButton(Rect r, string name, string tip, string key)
        {
            bool hov = r.Contains(_mouse);
            Box(r, _sCard);
            if (hov) Fill(new Rect(r.x + 14f, r.y + 1f, r.width - 28f, 1f), new Color(1f, 1f, 1f, 0.18f));
            GUI.Label(new Rect(r.x + 18f, r.y + 8f, r.width - 170f, 20f), name, hov ? _stRow : _stRow);
            if (tip != null && tip.Length > 0)
                GUI.Label(new Rect(r.x + 18f, r.y + 27f, r.width - 170f, 16f), tip, _stTip);
            if (key != null && key.Length > 0)
            {
                float kw = 12f + key.Length * 7f;
                Rect kr = new Rect(r.x + r.width - 18f - kw, r.y + (r.height - 22f) * 0.5f, kw, 22f);
                Box(kr, _sCat);
                GUI.Label(kr, key, _stKey);
            }
            if (hov) { _tip = tip; if (Input.GetMouseButtonDown(0)) ClickButton(name); }
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
            if (hit.Contains(_mouse) && Input.GetMouseButton(0))
                ClickSlider(name, Mathf.Lerp(min, max, Mathf.Clamp01((_mouse.x - bar.x) / bar.width)));
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
            else if (name == "散射（霰弹）") { Shot.Scatter = !Shot.Scatter; }
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
            GUI.color = new Color(1f, 1f, 1f, e);

            // 1) 全屏遮罩
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.015f, 0.035f, 0.46f * e));

            // 2) 外投影（放大 18px，下移 10px）
            Box(new Rect(p.x - 14f, p.y - 6f, p.width + 28f, p.height + 32f), _sShadow);

            // 3) 玻璃内部：裁剪到面板
            GUI.BeginGroup(new Rect(p.x, p.y, p.width, p.height));

            Rect local = new Rect(0, 0, p.width, p.height);

            // 3a) 抓屏模糊层（只取面板对应区域）
            Texture2D bt = Glass.Tex;
            if (bt != null && Glass.On)
            {
                float ux = p.x / Mathf.Max(1, Screen.width);
                float uy = 1f - (p.y + p.height) / Mathf.Max(1, Screen.height);
                float uw = p.width / Mathf.Max(1, Screen.width);
                float uh = p.height / Mathf.Max(1, Screen.height);
                Color bc = new Color(1f, 1f, 1f, 0.92f);
                Color o2 = GUI.color; GUI.color = bc;
                GUI.DrawTextureWithTexCoords(local, bt, new Rect(ux, uy, uw, uh));
                GUI.color = o2;
            }

            // 3b) 弥散光斑（面板内）
            Blobs.Draw(new Rect(-p.width * 0.15f, -p.height * 0.2f, p.width * 1.3f, p.height * 1.4f));

            // 3c) 玻璃渐变叠色
            Tex(local, _texGrad, Color.white);

            // 3d) 指针跟随光斑
            if (p.Contains(_mouse))
            {
                float sr = 320f;
                Tex(new Rect(_mouse.x - p.x - sr * 0.5f, _mouse.y - p.y - sr * 0.5f, sr, sr),
                    _texSpot, new Color(1f, 1f, 1f, 0.13f));
            }

            // 3e) 内部内容
            DrawContent(p, e);

            GUI.EndGroup();

            // 4) 物理 Rim Light 描边（不裁剪，压在内部之上）
            Box(new Rect(p.x, p.y, p.width, p.height), _sRim);

            GUI.color = old;
        }

        static void DrawContent(Rect p, float e)
        {
            float lx = 0f, ly = 0f;

            // 标题
            Fill(new Rect(30f, 30f, 3.5f, 32f), new Color(0.373f, 0.941f, 0.784f, 0.95f));
            GUI.Label(new Rect(44f, 24f, 520f, 26f), "WORST CLASS EVER", _stTitle);
            GUI.Label(new Rect(46f, 52f, 520f, 18f), "external helper   ·   v8.1   ·   " + KeyName() + " 开合", _stSub);

            // 关闭
            Rect close = new Rect(p.width - 62f, 26f, 34f, 34f);
            bool chov = close.Contains(new Vector2(_mouse.x - p.x, _mouse.y - p.y));
            if (chov) Box(close, _sPillOff);
            GUIStyle cs = chov ? Mk(16, new Color(1f, 0.45f, 0.48f, 1f)) : _stClose;
            cs.alignment = TextAnchor.MiddleCenter;
            GUI.Label(close, "×", cs);
            if (chov && Input.GetMouseButtonDown(0)) { Open = false; HackBehaviour.Menu = false; }

            // 分隔：只用一条极淡的 1px
            Fill(new Rect(30f, 86f, p.width - 60f, 1f), new Color(1f, 1f, 1f, 0.07f));

            // 左栏分类
            float catX = 30f, catY = 104f;
            for (int i = 0; i < Cats.Length; i++)
            {
                Rect cr = new Rect(catX, catY + i * 46f, 158f, 40f);
                bool sel = (i == Cat);
                Vector2 lm = new Vector2(_mouse.x - p.x, _mouse.y - p.y);
                bool hov = cr.Contains(lm);

                if (sel) Box(cr, _sCatOn);
                else if (hov) Box(cr, _sCat);

                GUI.Label(cr, Cats[i], sel ? _stCatOn : _stCatOff);
                if (hov && Input.GetMouseButtonDown(0)) Cat = i;
            }

            // 左栏底部状态
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
                        Unit u = kv.Value; if (u == null) continue;
                        int sub = (int)u.UnitSubType;
                        if (sub == 101) nt++; else if (sub == 201) ns++; else if (sub == 202) nn++;
                    }
                }
            }
            catch { }

            float fy = p.height - 128f;
            Fill(new Rect(30f, fy - 14f, 158f, 1f), new Color(1f, 1f, 1f, 0.07f));
            GUI.Label(new Rect(30f, fy, 158f, 15f), "身份", _stFoot);
            GUI.Label(new Rect(30f, fy + 15f, 158f, 20f), role, _stRow);
            GUI.Label(new Rect(30f, fy + 48f, 158f, 15f), "场上  师 / 生 / NPC", _stFoot);
            GUI.Label(new Rect(30f, fy + 63f, 158f, 20f), nt + " / " + ns + " / " + nn, _stRow);

            // 右栏
            float rx = 212f, ry = 104f, rw = p.width - rx - 30f;
            Vector2 lmo = new Vector2(_mouse.x - p.x, _mouse.y - p.y);
            _tip = "";

            float rh = 58f, gap = 9f;
            if (Cat == 0)
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, rh), "透视 ESP", "F1", HackBehaviour.Esp, "画出所有老师 / 学生 / NPC 的位置"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "骨骼透视", "F11", HackBehaviour.Bones, "只画躯干 + 两臂 + 两腿"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "模型染色", "", Skin.On, "学生绿 / 老师红，整个模型换色"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "染色透墙", "End", Skin.Wall, "被墙挡住也完整绘制"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "显示 NPC", "F10", HackBehaviour.ShowNpc, "把 NPC 学生也一并画出来"); y += rh + gap;
                RowSlider(new Rect(rx, y, rw, 58f), "距离上限", HackBehaviour.MaxDist, 50f, 400f, "m");
            }
            else if (Cat == 1)
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, rh), "加速 x2.5", "F8", HackPatches.Speed, "本地移动速度 × 2.5"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "无限冲刺", "F7", HackPatches.InfSprint, "冲刺不消耗体力"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "无限体力", "Home", Energy.On, "技能能量 9999 / 疲劳归零"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "御剑飞行", "PgUp", Fly.On, "WASD 平移 · 空格上升 · Ctrl 下降"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "第三人称", "", ThirdPerson.On, "把主相机往后拉，看自己的背面"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "自动旋转", "", AutoSpin.On, "自己原地匀速转圈"); y += rh + gap;
                RowSlider(new Rect(rx, y, rw, 58f), "第三人称距离", ThirdPerson.Distance, 1.5f, 8f, "m");
            }
            else if (Cat == 2)
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, rh), "散射（霰弹）", "F3", Shot.Scatter, "一次扔出 7 颗粉笔头"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "速射 + 连发", "F4", Shot.Rapid, "无冷却，按住左键 20 发/秒"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "攻击穿墙", "1", WallHack.On, "粉笔 / 黑板擦 / 剑气无视墙体"); y += rh + gap;
                RowButton(new Rect(rx, y, rw, rh), "传送到老师", "瞬间贴到最近的老师身后", "F5"); y += rh + gap;
                RowButton(new Rect(rx, y, rw, rh), "传送到学生", "瞬间贴到最近的学生身边", "F6");
            }
            else if (Cat == 3)
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, rh), "秒完成互动", "9", Instant.On, "写板书 / 吃零食 / 吹笛 全部瞬发"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "无限物品", "0", Instant.InfItems, "手上与嘴里的物品不消耗"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "无限投掷", "PgDn", Stock.On, "烟雾 / 闪光 不扣补货冷却"); y += rh + gap;
                RowButton(new Rect(rx, y, rw, rh), "秒解救同伴", "把柱子上和老师手上的同伴全部放下", "2"); y += rh + gap;
                RowButton(new Rect(rx, y, rw, rh), "立即脱困", "清掉被抓 / 挂柱 / 眩晕的全部状态", "Ins");
            }
            else
            {
                float y = ry;
                RowToggle(new Rect(rx, y, rw, rh), "不可被抓", "3", Protect.NoCatch, "老师的抓取判定直接失效"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "不可被打晕", "4", Protect.NoStun, "眩晕状态与屏幕特效都不进来"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "完全隐形", "Del", Ghost.On, "模型 + 名字 + 剪影全部消失"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "自动刷新状态", "R", Refresh.On, "每 0.08s 清掉被抓 / 眩晕 / 挂载"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "视角锁定", "5", StableCam.On, "眩晕不抖镜、不拉扯视角"); y += rh + gap;
                RowToggle(new Rect(rx, y, rw, rh), "防飞出地图", "", Safe.On, "Y 越界自动拉回地面"); y += rh + gap;
                RowButton(new Rect(rx, y, rw, rh), "刷新状态一次", "立刻执行一次完整刷新", "");
            }

            // 底部
            float by = p.height - 34f;
            Fill(new Rect(30f, by - 12f, p.width - 60f, 1f), new Color(1f, 1f, 1f, 0.07f));
            Tex(new Rect(30f, by + 4f, 6f, 6f), _texKnob, CMint);
            GUI.Label(new Rect(44f, by, 430f, 16f), _tip.Length > 0 ? _tip : "点击行切换开关  ·  拖动滑块调数值", _stFoot);

            // 呼出键（可自定义，全游戏唯一热键）
            Rect kb = new Rect(p.width - 300f, by - 6f, 132f, 26f);
            Vector2 lm2 = new Vector2(_mouse.x - p.x, _mouse.y - p.y);
            bool kbH = kb.Contains(lm2);
            Box(kb, Binding ? _sPillOn : (kbH ? _sCardOn : _sCat));
            GUI.Label(kb, Binding ? "按任意键…" : "呼出键 " + KeyName(), Binding ? _stCatOn : (kbH ? _stVal : _stCatOff));
            if (kbH && Input.GetMouseButtonDown(0) && !Binding) { Binding = true; _bindAt = Time.unscaledTime; }

            GUI.Label(new Rect(p.width - 156f, by, 126f, 16f),
                "FPS " + Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)), _stKey);
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
