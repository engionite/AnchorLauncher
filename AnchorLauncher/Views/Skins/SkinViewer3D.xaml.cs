using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace AnchorLauncher.Views.Skins;

/// <summary>
/// Real-time 3D Minecraft skin preview. Builds a humanoid from box <see cref="GeometryModel3D"/>s
/// (head 8³, body 8×12×4, arms/legs 4×12×4, total height 32, centred on Y). Each face is a quad
/// (2 triangles) UV-mapped to the exact pixel region on the 64×64 skin. The outer "overlay" layer
/// (hat/jacket/sleeves/pants) is alpha-composited onto the base texture (no WPF 3D-transparency
/// artifacts), and the whole texture is nearest-neighbour upscaled so it stays pixel-crisp.
/// Idle-spins on Y; drag to rotate.
/// </summary>
public partial class SkinViewer3D : UserControl
{
    private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0, 1, 0), 20);
    private bool   _dragging;
    private double _dragStartX;
    private double _dragStartAngle;

    private sealed record Rect(int X, int Y, int W, int H);
    private sealed record Box(double Cx, double Cy, double Cz, double W, double H, double D,
                              Rect[] Base, Rect[]? Overlay);   // faces order: Front,Back,Right,Left,Top,Bottom

    public SkinViewer3D()
    {
        InitializeComponent();
        ModelHost.Transform = new RotateTransform3D(_rotation, new Point3D(0, 16, 0));

        Loaded += (_, _) => StartIdleSpin();
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp   += OnUp;
        MouseMove           += OnMove;
    }

    /// <summary>Rebuilds the model. <paramref name="slim"/> renders the 3-wide (Alex) arms.</summary>
    public void SetSkin(BitmapSource? skin, bool slim = false)
    {
        try { ModelHost.Content = BuildModel(skin, slim); }
        catch (Exception ex) { Debug.WriteLine($"[SkinViewer3D] SetSkin failed: {ex}"); }
    }

    // ── Model assembly ───────────────────────────────────────────────────────

    private Model3DGroup BuildModel(BitmapSource? skin, bool slim)
    {
        var group = new Model3DGroup();
        bool legacy = (skin?.PixelHeight ?? 64) <= 32;

        // The metadata hint OR a texture-detected slim skin (covers sources without metadata).
        bool effectiveSlim = !legacy && (slim || DetectSlim(skin));

        if (skin == null)
        {
            var gray = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x42)));
            var placeholder = new MeshGeometry3D();
            foreach (var box in Parts(false, false)) AddBox(placeholder, box.Base, box, 0.0);
            group.Children.Add(new GeometryModel3D(placeholder, gray) { BackMaterial = gray });
            return group;
        }

        // One crisp (nearest-neighbour-upscaled) texture shared by both layers.
        var texture = UpscaleNearest(skin);
        var brush   = new ImageBrush(texture) { Stretch = Stretch.Fill, TileMode = TileMode.None };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        Material material = new DiffuseMaterial(brush);

        // BASE layer — opaque, normal size, rendered first.
        var baseMesh = new MeshGeometry3D();
        foreach (var box in Parts(legacy, effectiveSlim))
            AddBox(baseMesh, box.Base, box, 0.0);
        group.Children.Add(new GeometryModel3D(baseMesh, material) { BackMaterial = material });

        // OVERLAY layer (hat/jacket/sleeves/pants) — "puffy": slightly larger boxes, transparent
        // where the second layer is empty, rendered AFTER the base. Material-only so back faces are
        // culled → the transparent texels show the base behind them instead of punching holes.
        var overMesh = new MeshGeometry3D();
        foreach (var box in Parts(legacy, effectiveSlim))
            if (box.Overlay != null) AddBox(overMesh, box.Overlay, box, 0.5);
        if (overMesh.Positions.Count > 0)
            group.Children.Add(new GeometryModel3D(overMesh, material));

        return group;
    }

    /// <summary>Texture-based slim guess: the classic-only arm columns are transparent on a slim skin.</summary>
    private static bool DetectSlim(BitmapSource? skin)
    {
        try
        {
            if (skin == null || skin.PixelWidth < 64 || skin.PixelHeight < 64) return false;
            var src = new FormatConvertedBitmap(skin, PixelFormats.Bgra32, null, 0);
            var px = new byte[4];
            byte AlphaAt(int x, int y)
            {
                src.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), px, 4, 0);
                return px[3];
            }
            // (47,16) right-arm + (39,48) left-arm: 4th column of the top strip → blank on slim skins
            return AlphaAt(47, 16) == 0 && AlphaAt(39, 48) == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Base + overlay UV regions for each body part. Coordinates are the exact pixel rects on the
    /// modern 64×64 layout. Face order is Front, Back, Right, Left, Top, Bottom.
    /// </summary>
    private static IEnumerable<Box> Parts(bool legacy, bool slim)
    {
        static Rect[] F(params (int x, int y, int w, int h)[] r) => r.Select(t => new Rect(t.x, t.y, t.w, t.h)).ToArray();

        // HEAD 8×8×8 (centre y=28) + hat overlay
        yield return new Box(0, 28, 0, 8, 8, 8,
            F((8,8,8,8), (24,8,8,8), (0,8,8,8), (16,8,8,8), (8,0,8,8), (16,0,8,8)),
            F((40,8,8,8),(56,8,8,8),(32,8,8,8),(48,8,8,8),(40,0,8,8),(48,0,8,8)));

        if (legacy)
        {
            // Pre-1.8 (64×32): only base + hat; mirror the right limbs for the left.
            yield return new Box(0, 18, 0, 8, 12, 4, F((20,20,8,12),(32,20,8,12),(16,20,4,12),(28,20,4,12),(20,16,8,4),(28,16,8,4)), null);
            yield return new Box(-6, 18, 0, 4, 12, 4, F((44,20,4,12),(52,20,4,12),(40,20,4,12),(48,20,4,12),(44,16,4,4),(48,16,4,4)), null);
            yield return new Box( 6, 18, 0, 4, 12, 4, F((44,20,4,12),(52,20,4,12),(40,20,4,12),(48,20,4,12),(44,16,4,4),(48,16,4,4)), null);
            yield return new Box(-2,  6, 0, 4, 12, 4, F((4,20,4,12),(12,20,4,12),(0,20,4,12),(8,20,4,12),(4,16,4,4),(8,16,4,4)), null);
            yield return new Box( 2,  6, 0, 4, 12, 4, F((4,20,4,12),(12,20,4,12),(0,20,4,12),(8,20,4,12),(4,16,4,4),(8,16,4,4)), null);
            yield break;
        }

        // BODY 8×12×4 (centre y=18) + jacket
        yield return new Box(0, 18, 0, 8, 12, 4,
            F((20,20,8,12),(32,20,8,12),(16,20,4,12),(28,20,4,12),(20,16,8,4),(28,16,8,4)),
            F((20,36,8,12),(32,36,8,12),(16,36,4,12),(28,36,4,12),(20,32,8,4),(28,32,8,4)));

        if (slim)
        {
            // Alex model: 3-wide arms. Front/Back are 3px, Right/Left (depth) are 4px.
            // RIGHT ARM 3×12×4 (centre x=-5.5) + sleeve
            yield return new Box(-5.5, 18, 0, 3, 12, 4,
                F((44,20,3,12),(51,20,3,12),(40,20,4,12),(47,20,4,12),(44,16,3,4),(47,16,3,4)),
                F((44,36,3,12),(51,36,3,12),(40,36,4,12),(47,36,4,12),(44,32,3,4),(47,32,3,4)));
            // LEFT ARM 3×12×4 (centre x=+5.5) + sleeve
            yield return new Box(5.5, 18, 0, 3, 12, 4,
                F((36,52,3,12),(43,52,3,12),(32,52,4,12),(39,52,4,12),(36,48,3,4),(39,48,3,4)),
                F((52,52,3,12),(59,52,3,12),(48,52,4,12),(55,52,4,12),(52,48,3,4),(55,48,3,4)));
        }
        else
        {
            // Steve model: 4-wide arms.
            // RIGHT ARM 4×12×4 (centre x=-6) + sleeve
            yield return new Box(-6, 18, 0, 4, 12, 4,
                F((44,20,4,12),(52,20,4,12),(40,20,4,12),(48,20,4,12),(44,16,4,4),(48,16,4,4)),
                F((44,36,4,12),(52,36,4,12),(40,36,4,12),(48,36,4,12),(44,32,4,4),(48,32,4,4)));
            // LEFT ARM 4×12×4 (centre x=+6) + sleeve
            yield return new Box(6, 18, 0, 4, 12, 4,
                F((36,52,4,12),(44,52,4,12),(32,52,4,12),(40,52,4,12),(36,48,4,4),(40,48,4,4)),
                F((52,52,4,12),(60,52,4,12),(48,52,4,12),(56,52,4,12),(52,48,4,4),(56,48,4,4)));
        }
        // RIGHT LEG 4×12×4 (centre x=-2) + pants
        yield return new Box(-2, 6, 0, 4, 12, 4,
            F((4,20,4,12),(12,20,4,12),(0,20,4,12),(8,20,4,12),(4,16,4,4),(8,16,4,4)),
            F((4,36,4,12),(12,36,4,12),(0,36,4,12),(8,36,4,12),(4,32,4,4),(8,32,4,4)));
        // LEFT LEG 4×12×4 (centre x=+2) + pants
        yield return new Box(2, 6, 0, 4, 12, 4,
            F((20,52,4,12),(28,52,4,12),(16,52,4,12),(24,52,4,12),(20,48,4,4),(24,48,4,4)),
            F((4,52,4,12),(12,52,4,12),(0,52,4,12),(8,52,4,12),(4,48,4,4),(8,48,4,4)));
    }

    private const int Tex = 64;   // logical texture size for UV normalisation

    /// <summary>Adds a box using the given 6 face UV rects, optionally inflated (for the puffy overlay).</summary>
    private static void AddBox(MeshGeometry3D m, Rect[] f, Box box, double inflate)
    {
        double cx = box.Cx, cy = box.Cy, cz = box.Cz;
        double hx = box.W / 2 + inflate, hy = box.H / 2 + inflate, hz = box.D / 2 + inflate;

        // 8 corners
        var A = new Point3D(cx - hx, cy + hy, cz + hz); // left  top    front
        var B = new Point3D(cx + hx, cy + hy, cz + hz); // right top    front
        var C = new Point3D(cx + hx, cy - hy, cz + hz); // right bottom front
        var D = new Point3D(cx - hx, cy - hy, cz + hz); // left  bottom front
        var E = new Point3D(cx - hx, cy + hy, cz - hz); // left  top    back
        var Fp= new Point3D(cx + hx, cy + hy, cz - hz); // right top    back
        var G = new Point3D(cx + hx, cy - hy, cz - hz); // right bottom back
        var H = new Point3D(cx - hx, cy - hy, cz - hz); // left  bottom back

        // Each face passes its corners as (top-left, top-right, bottom-right, bottom-left)
        // as seen from outside, plus the outward normal.
        AddFace(m, A, B, C, D, new Vector3D(0, 0, 1),  f[0]); // Front  +Z
        AddFace(m, Fp, E, H, G, new Vector3D(0, 0, -1), f[1]); // Back   -Z
        AddFace(m, B, Fp, G, C, new Vector3D(1, 0, 0),  f[2]); // Right  +X
        AddFace(m, E, A, D, H, new Vector3D(-1, 0, 0), f[3]); // Left   -X
        AddFace(m, E, Fp, B, A, new Vector3D(0, 1, 0),  f[4]); // Top    +Y
        AddFace(m, D, C, G, H, new Vector3D(0, -1, 0), f[5]); // Bottom -Y
    }

    private static void AddFace(MeshGeometry3D m, Point3D tl, Point3D tr, Point3D br, Point3D bl,
                                Vector3D normal, Rect uv)
    {
        int b = m.Positions.Count;
        m.Positions.Add(tl); m.Positions.Add(tr); m.Positions.Add(br); m.Positions.Add(bl);
        for (int i = 0; i < 4; i++) m.Normals.Add(normal);

        // u = pixelX/64, v = pixelY/64 (a hair of inset avoids sampling the neighbouring region)
        double u0 = (uv.X + 0.02) / Tex, v0 = (uv.Y + 0.02) / Tex;
        double u1 = (uv.X + uv.W - 0.02) / Tex, v1 = (uv.Y + uv.H - 0.02) / Tex;
        m.TextureCoordinates.Add(new Point(u0, v0)); // TL
        m.TextureCoordinates.Add(new Point(u1, v0)); // TR
        m.TextureCoordinates.Add(new Point(u1, v1)); // BR
        m.TextureCoordinates.Add(new Point(u0, v1)); // BL

        // CCW winding so the front Material faces outward: (TL,BL,BR) + (TL,BR,TR)
        m.TriangleIndices.Add(b);     m.TriangleIndices.Add(b + 3); m.TriangleIndices.Add(b + 2);
        m.TriangleIndices.Add(b);     m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 1);
    }

    // ── Texture: nearest-neighbour upscale so the 64-px skin stays pixel-crisp ──

    private static BitmapSource UpscaleNearest(BitmapSource skin)
    {
        try
        {
            var src = new FormatConvertedBitmap(skin, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var buf = new byte[h * stride];
            src.CopyPixels(buf, stride, 0);

            const int S = 8;
            int W2 = w * S, H2 = h * S, stride2 = W2 * 4;
            var big = new byte[H2 * stride2];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int si = y * stride + x * 4;
                byte b0 = buf[si], g0 = buf[si + 1], r0 = buf[si + 2], a0 = buf[si + 3];
                for (int dy = 0; dy < S; dy++)
                {
                    int row = ((y * S + dy) * W2 + x * S) * 4;
                    for (int dx = 0; dx < S; dx++)
                    {
                        int di = row + dx * 4;
                        big[di] = b0; big[di + 1] = g0; big[di + 2] = r0; big[di + 3] = a0;
                    }
                }
            }

            var outBmp = BitmapSource.Create(W2, H2, 96, 96, PixelFormats.Bgra32, null, big, stride2);
            outBmp.Freeze();
            return outBmp;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinViewer3D] UpscaleNearest failed: {ex.Message}");
            return skin;
        }
    }

    // ── Rotation: idle spin + mouse drag ─────────────────────────────────────

    private void StartIdleSpin()
    {
        var anim = new DoubleAnimation
        {
            By             = 360,
            Duration       = new Duration(TimeSpan.FromSeconds(8)),
            RepeatBehavior = RepeatBehavior.Forever
        };
        _rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, anim);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging       = true;
        _dragStartX     = e.GetPosition(this).X;
        _dragStartAngle = _rotation.Angle;
        _rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        _rotation.Angle = _dragStartAngle;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var dx = e.GetPosition(this).X - _dragStartX;
        _rotation.Angle = _dragStartAngle + dx * 0.6;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        StartIdleSpin();
    }
}
