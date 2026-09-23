using System.Numerics;
using Raylib_cs;

namespace LoranVisual;

/// <summary>
/// Educational LORAN-C style chain demo.
/// Shows pulse timing in slow motion, measured time differences, then the
/// hyperbolic lines of position that intersect at the receiver.
/// </summary>
public static class Program {
  // Map units are kilometres. RF speed is scaled way down so the sequence is visible.
  const float RealCKmPerUs = 0.299792458f; // km per microsecond (real light)
  const float SimC = 80f;                  // km per sim-second (dramatic slow-mo)

  const int ScreenW = 1400;
  const int ScreenH = 900;
  const int HudH = 170;

  enum Phase {
    Ready,
    MasterTx,
    SecondaryXTx,
    SecondaryYTx,
    Measure,
    Hyperbolas,
    Done
  }

  struct Station {
    public string Label;
    public string Role;
    public Vector2 Pos;
    public Color Color;
    public float TxTime; // when this station transmits (sim seconds), -1 if not yet
  }

  struct Pulse {
    public Vector2 Origin;
    public float Birth;     // sim time
    public Color Color;
    public int StationIndex; // 0=M, 1=X, 2=Y
  }

  static void Main() {
    Raylib.SetConfigFlags(ConfigFlags.FLAG_MSAA_4X_HINT | ConfigFlags.FLAG_WINDOW_HIGHDPI);
    Raylib.InitWindow(ScreenW, ScreenH, "LORAN Chain — How a Fix Works");
    Raylib.SetTargetFPS(60);
    Raylib.SetExitKey(KeyboardKey.KEY_NULL);

    // Chain geometry (km). Baseline Master↔X / Master↔Y typical of a coastal chain.
    var stations = new Station[]
    {
            new() { Label = "M",  Role = "Master",       Pos = new Vector2(180, 420), Color = new Color(255, 196, 72, 255),  TxTime = -1 },
            new() { Label = "X",  Role = "Secondary X",  Pos = new Vector2(620, 160), Color = new Color(64, 210, 220, 255),  TxTime = -1 },
            new() { Label = "Y",  Role = "Secondary Y",  Pos = new Vector2(720, 620), Color = new Color(255, 120, 90, 255),  TxTime = -1 },
    };

    Vector2 receiver = new(430, 380);
    bool dragging = false;

    // Coding delays (extra intentional wait after master's wave would reach the secondary).
    // Real LORAN emission delay = baseline delay + coding delay.
    float codingDelayX = 1.4f; // sim seconds
    float codingDelayY = 2.6f;

    var pulses = new List<Pulse>();
    float simTime = 0;
    float speed = 1f;
    bool playing = false;
    Phase phase = Phase.Ready;

    float rxHearM = -1, rxHearX = -1, rxHearY = -1;
    bool hyperbolaReveal = false;

    Camera2D cam = new() {
      target = new Vector2(450, 400),
      offset = new Vector2(ScreenW * 0.42f, (ScreenH - HudH) * 0.5f),
      zoom = 1.05f,
      rotation = 0
    };

    void ResetSequence(bool keepPlaying) {
      simTime = 0;
      pulses.Clear();
      for (int i = 0; i < stations.Length; i++) {
        var s = stations[i];
        s.TxTime = -1;
        stations[i] = s;
      }
      rxHearM = rxHearX = rxHearY = -1;
      hyperbolaReveal = false;
      phase = Phase.Ready;
      playing = keepPlaying;
      if (keepPlaying) {
        // Kick off immediately
        FireMaster();
      }
    }

    void FireMaster() {
      var m = stations[0];
      m.TxTime = simTime;
      stations[0] = m;
      pulses.Add(new Pulse { Origin = m.Pos, Birth = simTime, Color = m.Color, StationIndex = 0 });
      phase = Phase.MasterTx;
    }

    float EmissionDelay(int secondaryIndex) {
      float baseline = Vector2.Distance(stations[0].Pos, stations[secondaryIndex].Pos) / SimC;
      return baseline + (secondaryIndex == 1 ? codingDelayX : codingDelayY);
    }

    // Geometric path difference slave - master at a point (km).
    float PathDiff(Vector2 p, int secondaryIndex) =>
        Vector2.Distance(p, stations[secondaryIndex].Pos) - Vector2.Distance(p, stations[0].Pos);

    while (!Raylib.WindowShouldClose()) {
      float dt = Raylib.GetFrameTime();

      // --- Input ---
      if (Raylib.IsKeyPressed(KeyboardKey.KEY_SPACE)) {
        if (phase is Phase.Ready or Phase.Done)
          ResetSequence(true);
        else
          playing = !playing;
      }
      if (Raylib.IsKeyPressed(KeyboardKey.KEY_R))
        ResetSequence(playing || phase != Phase.Ready);
      if (Raylib.IsKeyPressed(KeyboardKey.KEY_LEFT_BRACKET))
        speed = Math.Max(0.25f, speed / 1.5f);
      if (Raylib.IsKeyPressed(KeyboardKey.KEY_RIGHT_BRACKET))
        speed = Math.Min(8f, speed * 1.5f);
      if (Raylib.IsKeyPressed(KeyboardKey.KEY_ONE)) speed = 0.5f;
      if (Raylib.IsKeyPressed(KeyboardKey.KEY_TWO)) speed = 1f;
      if (Raylib.IsKeyPressed(KeyboardKey.KEY_THREE)) speed = 2.5f;

      float wheel = Raylib.GetMouseWheelMove();
      if (wheel != 0) {
        cam.zoom = Math.Clamp(cam.zoom + wheel * 0.08f, 0.45f, 2.5f);
      }

      Vector2 mouse = Raylib.GetMousePosition();
      Vector2 worldMouse = Raylib.GetScreenToWorld2D(mouse, cam);
      bool mouseInMap = mouse.Y < ScreenH - HudH && mouse.Y > 64;

      if (mouseInMap && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT)) {
        // Click anywhere on the map to place the receiver
        receiver = worldMouse;
        dragging = true;
        if (phase != Phase.Ready)
          ResetSequence(playing);
      }
      if (Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT))
        dragging = false;
      if (dragging && mouseInMap && Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT)) {
        if (Vector2.Distance(receiver, worldMouse) > 0.5f) {
          receiver = worldMouse;
          if (phase != Phase.Ready)
            ResetSequence(playing);
        }
      }

      // Pan with middle mouse or arrows / WASD
      Vector2 pan = Vector2.Zero;
      if (Raylib.IsKeyDown(KeyboardKey.KEY_W) || Raylib.IsKeyDown(KeyboardKey.KEY_UP)) pan.Y -= 1;
      if (Raylib.IsKeyDown(KeyboardKey.KEY_S) || Raylib.IsKeyDown(KeyboardKey.KEY_DOWN)) pan.Y += 1;
      if (Raylib.IsKeyDown(KeyboardKey.KEY_A) || Raylib.IsKeyDown(KeyboardKey.KEY_LEFT)) pan.X -= 1;
      if (Raylib.IsKeyDown(KeyboardKey.KEY_D) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT)) pan.X += 1;
      if (pan != Vector2.Zero)
        cam.target += Vector2.Normalize(pan) * (220f / cam.zoom) * dt;

      // --- Simulation ---
      if (playing && phase is not Phase.Done and not Phase.Ready) {
        simTime += dt * speed;

        // Secondaries fire on emission-delay schedule after master
        if (stations[0].TxTime >= 0) {
          for (int i = 1; i <= 2; i++) {
            if (stations[i].TxTime < 0 && simTime >= stations[0].TxTime + EmissionDelay(i)) {
              var s = stations[i];
              s.TxTime = simTime;
              stations[i] = s;
            pulses.Add(new Pulse {
              Origin = s.Pos,
              Birth = simTime,
              Color = s.Color,
              StationIndex = i
            });
            phase = i == 1 ? Phase.SecondaryXTx : Phase.SecondaryYTx;
            }
          }
        }

        // Receiver hearing times (first arrival of each station's pulse)
        foreach (var p in pulses) {
          float travel = Vector2.Distance(p.Origin, receiver) / SimC;
          float eta = p.Birth + travel;
          if (simTime < eta) continue;

          if (p.StationIndex == 0 && rxHearM < 0) rxHearM = eta;
          else if (p.StationIndex == 1 && rxHearX < 0) rxHearX = eta;
          else if (p.StationIndex == 2 && rxHearY < 0) rxHearY = eta;
        }

        if (rxHearM >= 0 && rxHearX >= 0 && rxHearY >= 0 && phase is Phase.SecondaryYTx or Phase.SecondaryXTx or Phase.MasterTx) {
          // Small beat after last reception before measure callout
          float last = Math.Max(rxHearM, Math.Max(rxHearX, rxHearY));
          if (simTime >= last + 0.35f)
            phase = Phase.Measure;
        }

        if (phase == Phase.Measure && simTime >= Math.Max(rxHearM, Math.Max(rxHearX, rxHearY)) + 1.6f) {
          phase = Phase.Hyperbolas;
          hyperbolaReveal = true;
        }

        if (phase == Phase.Hyperbolas && simTime >= Math.Max(rxHearM, Math.Max(rxHearX, rxHearY)) + 3.2f) {
          phase = Phase.Done;
          playing = false;
        }
      }

      // Measured TDs and geometric constants for hyperbolas
      float tdX = (rxHearX >= 0 && rxHearM >= 0) ? rxHearX - rxHearM : float.NaN;
      float tdY = (rxHearY >= 0 && rxHearM >= 0) ? rxHearY - rxHearM : float.NaN;
      float geoX = PathDiff(receiver, 1); // km, slave - master
      float geoY = PathDiff(receiver, 2);
      // What the receiver derives after subtracting known emission delay:
      // pathDiff = c * (TD - ED)
      float derivedX = float.IsNaN(tdX) ? float.NaN : SimC * (tdX - EmissionDelay(1));
      float derivedY = float.IsNaN(tdY) ? float.NaN : SimC * (tdY - EmissionDelay(2));

      // --- Draw ---
      Raylib.BeginDrawing();
      Raylib.ClearBackground(new Color(8, 14, 24, 255));

      Raylib.BeginMode2D(cam);
      DrawOceanGrid(1200, 900);
      DrawBaselines(stations);

      // Pulses (expanding RF wavefronts)
      foreach (var p in pulses) {
        float age = Math.Max(0, simTime - p.Birth);
        float radius = age * SimC;
        float fade = Math.Clamp(1.2f - age * 0.12f, 0.15f, 1f);
        var ring = new Color(p.Color.r, p.Color.g, p.Color.b, (byte)(90 * fade));
        var ringBright = new Color(p.Color.r, p.Color.g, p.Color.b, (byte)(200 * fade));
        Raylib.DrawCircleLines((int)p.Origin.X, (int)p.Origin.Y, radius, ringBright);
        Raylib.DrawCircleLines((int)p.Origin.X, (int)p.Origin.Y, radius - 1.5f, ring);
        // Soft fill hint near the wavefront
        if (radius > 4)
          Raylib.DrawCircleLines((int)p.Origin.X, (int)p.Origin.Y, radius * 0.985f, ring);
      }

      // Hyperbolas after measurement
      if (hyperbolaReveal && !float.IsNaN(geoX) && !float.IsNaN(geoY)) {
        DrawHyperbolaBranch(stations[0].Pos, stations[1].Pos, geoX, stations[1].Color, receiver, 0);
        DrawHyperbolaBranch(stations[0].Pos, stations[2].Pos, geoY, stations[2].Color, receiver, 16);
        // Fix marker
        Raylib.DrawCircleLines((int)receiver.X, (int)receiver.Y, 22, new Color(255, 220, 140, 220));
        Raylib.DrawCircleLines((int)receiver.X, (int)receiver.Y, 28, new Color(255, 220, 140, 120));
        Raylib.DrawText("FIX", (int)receiver.X - 12, (int)receiver.Y + 32, 16, new Color(255, 220, 140, 255));
      }

      // Stations
      for (int i = 0; i < stations.Length; i++)
        DrawStation(stations[i], stations[i].TxTime >= 0 && simTime - stations[i].TxTime < 0.45f);

      // Receiver
      DrawReceiver(receiver, dragging);

      // Travel-time leader lines while pulses are in flight toward receiver
      if (playing || phase is Phase.Measure or Phase.Hyperbolas or Phase.Done) {
        foreach (var p in pulses) {
          float travel = Vector2.Distance(p.Origin, receiver) / SimC;
          float eta = p.Birth + travel;
          if (simTime < p.Birth || simTime > eta + 0.05f) continue;
          float t = Math.Clamp((simTime - p.Birth) / travel, 0, 1);
          Vector2 tip = Vector2.Lerp(p.Origin, receiver, t);
          var c = new Color(p.Color.r, p.Color.g, p.Color.b, (byte)160);
          Raylib.DrawLineEx(p.Origin, tip, 1.5f, c);
          Raylib.DrawCircleV(tip, 4, c);
        }
      }

      Raylib.EndMode2D();

      DrawHud(phase, speed, playing, stations, receiver, simTime,
          rxHearM, rxHearX, rxHearY, tdX, tdY, EmissionDelay(1), EmissionDelay(2),
          derivedX, derivedY, geoX, geoY, hyperbolaReveal);

      Raylib.EndDrawing();
    }

    Raylib.CloseWindow();
  }

  static void DrawOceanGrid(float w, float h) {
    var water = new Color(12, 28, 42, 255);
    Raylib.DrawRectangle(-200, -200, (int)w + 400, (int)h + 400, water);

    var major = new Color(40, 70, 88, 255);
    var minor = new Color(22, 42, 56, 255);
    for (int x = 0; x <= 1000; x += 40)
      Raylib.DrawLine(x, 0, x, 800, x % 200 == 0 ? major : minor);
    for (int y = 0; y <= 800; y += 40)
      Raylib.DrawLine(0, y, 1000, y, y % 200 == 0 ? major : minor);
  }

  static void DrawBaselines(Station[] stations) {
    var baseCol = new Color(120, 140, 160, 70);
    Raylib.DrawLineEx(stations[0].Pos, stations[1].Pos, 1.5f, baseCol);
    Raylib.DrawLineEx(stations[0].Pos, stations[2].Pos, 1.5f, baseCol);
    DrawLabelMid(stations[0].Pos, stations[1].Pos, $"baseline M–X  {Vector2.Distance(stations[0].Pos, stations[1].Pos):0} km", stations[1].Color);
    DrawLabelMid(stations[0].Pos, stations[2].Pos, $"baseline M–Y  {Vector2.Distance(stations[0].Pos, stations[2].Pos):0} km", stations[2].Color);
  }

  static void DrawLabelMid(Vector2 a, Vector2 b, string text, Color tint) {
    Vector2 mid = (a + b) * 0.5f;
    var col = new Color(tint.r, tint.g, tint.b, (byte)160);
    Raylib.DrawText(text, (int)mid.X - 70, (int)mid.Y - 14, 12, col);
  }

  static void DrawStation(Station s, bool flash) {
    float r = flash ? 16f : 11f;
    Raylib.DrawCircleV(s.Pos, r + 6, new Color(s.Color.r, s.Color.g, s.Color.b, (byte)40));
    Raylib.DrawCircleV(s.Pos, r, s.Color);
    Raylib.DrawCircleLines((int)s.Pos.X, (int)s.Pos.Y, r + 2, Color.WHITE);
    Raylib.DrawText(s.Label, (int)s.Pos.X - 5, (int)s.Pos.Y - 6, 14, new Color(10, 14, 20, 255));
    Raylib.DrawText(s.Role, (int)s.Pos.X + 16, (int)s.Pos.Y - 8, 14, s.Color);
  }

  static void DrawReceiver(Vector2 pos, bool active) {
    var hull = active ? new Color(255, 255, 255, 255) : new Color(230, 235, 245, 255);
    // Simple vessel mark
    Raylib.DrawCircleV(pos, 14, new Color(255, 255, 255, 25));
    Raylib.DrawCircleV(pos, 7, hull);
    Raylib.DrawTriangle(
        pos + new Vector2(0, -16),
        pos + new Vector2(-8, 4),
        pos + new Vector2(8, 4),
        hull);
    Raylib.DrawText("RECEIVER", (int)pos.X + 14, (int)pos.Y - 18, 13, new Color(220, 230, 240, 220));
    Raylib.DrawText("click map to place", (int)pos.X + 14, (int)pos.Y - 2, 12, new Color(140, 160, 180, 200));
  }

  /// <summary>
  /// Hyperbola: d(P, focusB) - d(P, focusA) = delta (signed path difference).
  /// </summary>
  static void DrawHyperbolaBranch(Vector2 focusA, Vector2 focusB, float delta, Color color, Vector2 nearPoint, int labelNudge) {
    float twoA = Math.Abs(delta);
    float twoC = Vector2.Distance(focusA, focusB);
    if (twoA < 0.5f || twoA >= twoC - 0.5f) return;

    float a = twoA * 0.5f;
    float c = twoC * 0.5f;
    float b = MathF.Sqrt(c * c - a * a);
    Vector2 center = (focusA + focusB) * 0.5f;
    float angle = MathF.Atan2(focusB.Y - focusA.Y, focusB.X - focusA.X);
    // Local +x points A→B. Branch around A when delta>0 (closer to A).
    float side = delta >= 0 ? -1f : 1f;
    float cos = MathF.Cos(angle);
    float sin = MathF.Sin(angle);

    var line = new Color(color.r, color.g, color.b, (byte)210);
    var glow = new Color(color.r, color.g, color.b, (byte)70);

    Vector2 Prev = default;
    bool hasPrev = false;
    for (float t = -3.0f; t <= 3.0f; t += 0.035f) {
      float lx = side * a * MathF.Cosh(t);
      float ly = b * MathF.Sinh(t);
      Vector2 p = center + new Vector2(lx * cos - ly * sin, lx * sin + ly * cos);
      // Keep on the chart
      if (p.X < -80 || p.X > 1080 || p.Y < -80 || p.Y > 880) {
        hasPrev = false;
        continue;
      }
      if (hasPrev) {
        Raylib.DrawLineEx(Prev, p, 5f, glow);
        Raylib.DrawLineEx(Prev, p, 2.2f, line);
      }
      Prev = p;
      hasPrev = true;
    }

    Raylib.DrawText("line of position", (int)nearPoint.X + 18, (int)nearPoint.Y + 20 + labelNudge, 12,
        new Color(color.r, color.g, color.b, (byte)220));
  }

  static void DrawHud(
      Phase phase, float speed, bool playing, Station[] stations, Vector2 receiver, float simTime,
      float hearM, float hearX, float hearY, float tdX, float tdY, float edX, float edY,
      float derivedX, float derivedY, float geoX, float geoY, bool showHyps) {
    int top = ScreenH - HudH;
    Raylib.DrawRectangle(0, 0, ScreenW, 64, new Color(6, 10, 18, 230));
    Raylib.DrawRectangle(0, top, ScreenW, HudH, new Color(6, 10, 18, 245));
    Raylib.DrawLine(0, top, ScreenW, top, new Color(50, 80, 100, 255));

    Raylib.DrawText("LORAN CHAIN FIX", 24, 16, 28, new Color(230, 236, 245, 255));
    Raylib.DrawText(PhaseCaption(phase), 24, 44, 16, new Color(160, 190, 210, 255));

    string controls = "SPACE play/pause   R replay   [ ] speed   click map to place receiver   scroll zoom   WASD pan";
    Raylib.DrawText(controls, ScreenW - 720, 22, 14, new Color(120, 140, 160, 255));
    Raylib.DrawText($"speed ×{speed:0.00}   t = {simTime:0.00}s (sim)", ScreenW - 720, 42, 14, new Color(120, 140, 160, 255));

    // Timeline
    int tlX = 40, tlY = top + 36, tlW = ScreenW - 380;
    Raylib.DrawText("PULSE TIMELINE", tlX, top + 12, 14, new Color(140, 160, 180, 255));
    Raylib.DrawRectangle(tlX, tlY, tlW, 8, new Color(30, 45, 60, 255));

    float tMax = Math.Max(8f, Math.Max(hearM, Math.Max(hearX, hearY)) + 2f);
    if (stations[0].TxTime >= 0) tMax = Math.Max(tMax, simTime + 0.5f);
    float Xof(float t) => tlX + Math.Clamp(t / tMax, 0, 1) * tlW;

    // Emission markers
    if (stations[0].TxTime >= 0)
      DrawTick(Xof(stations[0].TxTime), tlY, stations[0].Color, "M TX");
    if (stations[1].TxTime >= 0)
      DrawTick(Xof(stations[1].TxTime), tlY, stations[1].Color, "X TX");
    if (stations[2].TxTime >= 0)
      DrawTick(Xof(stations[2].TxTime), tlY, stations[2].Color, "Y TX");

    // Reception markers
    if (hearM >= 0) DrawTick(Xof(hearM), tlY + 28, stations[0].Color, "RX←M");
    if (hearX >= 0) DrawTick(Xof(hearX), tlY + 28, stations[1].Color, "RX←X");
    if (hearY >= 0) DrawTick(Xof(hearY), tlY + 28, stations[2].Color, "RX←Y");

    // Playhead
    float playX = Xof(simTime);
    Raylib.DrawLine((int)playX, tlY - 8, (int)playX, tlY + 48, new Color(255, 255, 255, 180));

    // TD braces once measured
    if (hearM >= 0 && hearX >= 0) {
      float a = Xof(hearM), b = Xof(hearX);
      Raylib.DrawLineEx(new Vector2(a, tlY + 52), new Vector2(b, tlY + 52), 2, stations[1].Color);
      Raylib.DrawText($"TD(X) = {tdX:0.00}s", (int)((a + b) / 2) - 40, tlY + 56, 13, stations[1].Color);
    }
    if (hearM >= 0 && hearY >= 0) {
      float a = Xof(hearM), b = Xof(hearY);
      Raylib.DrawLineEx(new Vector2(a, tlY + 74), new Vector2(b, tlY + 74), 2, stations[2].Color);
      Raylib.DrawText($"TD(Y) = {tdY:0.00}s", (int)((a + b) / 2) - 40, tlY + 78, 13, stations[2].Color);
    }

    // Side numbers
    int sx = ScreenW - 320;
    int sy = top + 14;
    Raylib.DrawText("NUMBERS", sx, sy, 14, new Color(140, 160, 180, 255));
    sy += 22;
    Raylib.DrawText($"Emission delay X  {edX:0.00}s", sx, sy, 14, stations[1].Color); sy += 18;
    Raylib.DrawText($"Emission delay Y  {edY:0.00}s", sx, sy, 14, stations[2].Color); sy += 22;

    if (!float.IsNaN(tdX)) {
      Raylib.DrawText($"path Δ X  c·(TD−ED) = {derivedX:0.0} km", sx, sy, 14, stations[1].Color); sy += 18;
      Raylib.DrawText($"  (true geometry {geoX:0.0} km)", sx, sy, 12, new Color(120, 140, 150, 255)); sy += 18;
    }
    if (!float.IsNaN(tdY)) {
      Raylib.DrawText($"path Δ Y  c·(TD−ED) = {derivedY:0.0} km", sx, sy, 14, stations[2].Color); sy += 18;
      Raylib.DrawText($"  (true geometry {geoY:0.0} km)", sx, sy, 12, new Color(120, 140, 150, 255)); sy += 18;
    }

    if (showHyps) {
      Raylib.DrawText("Two TDs → two hyperbolas → FIX", sx, sy + 4, 14, new Color(255, 220, 140, 255));
    }

    // Ready prompt
    if (phase == Phase.Ready) {
      string msg = "Press SPACE to transmit the master pulse";
      int tw = Raylib.MeasureText(msg, 22);
      Raylib.DrawText(msg, (ScreenW - tw) / 2, (ScreenH - HudH) / 2, 22, new Color(200, 220, 235, 230));
    }
    if (phase == Phase.Done) {
      string msg = "Fix complete — move the receiver and press R to replay";
      int tw = Raylib.MeasureText(msg, 20);
      Raylib.DrawRectangle((ScreenW - tw) / 2 - 16, 80, tw + 32, 36, new Color(0, 0, 0, 140));
      Raylib.DrawText(msg, (ScreenW - tw) / 2, 88, 20, new Color(255, 220, 140, 255));
    }

    // Suppress unused warning for RealC — kept as documentary constant
    _ = RealCKmPerUs;
  }

  static void DrawTick(float x, int y, Color c, string label) {
    Raylib.DrawLine((int)x, y - 6, (int)x, y + 14, c);
    Raylib.DrawCircle((int)x, y + 4, 3, c);
    Raylib.DrawText(label, (int)x + 4, y - 8, 12, c);
  }

  static string PhaseCaption(Phase phase) => phase switch {
    Phase.Ready => "Ready — a LORAN chain is a Master plus Secondary stations on known baselines",
    Phase.MasterTx => "1 · Master transmits a pulse group — radio wave expands at (scaled) light speed",
    Phase.SecondaryXTx => "2 · Secondary X transmits after its emission delay (baseline travel + coding delay)",
    Phase.SecondaryYTx => "3 · Secondary Y transmits later on its own emission-delay schedule",
    Phase.Measure => "4 · Receiver notes arrival times and measures TD(X) and TD(Y) from the master beat",
    Phase.Hyperbolas => "5 · Subtract known emission delays → path differences → hyperbolic lines of position",
    Phase.Done => "6 · Intersection of the two lines of position is your fix",
    _ => ""
  };
}
