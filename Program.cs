using Raylib_cs;
using System.Numerics;

namespace LoranVisual;

public class Program {
    static float C = 299792.458f;

    static float MicrosecondsToSeconds(float microseconds) {
        return microseconds / 1_000_000f;
    }

    static Vector2 CalculatePosition(Vector2 master, Vector2 slaveA, Vector2 slaveB, Vector2 timeDifference) {
        Vector2 distance = new Vector2(timeDifference.X * C, timeDifference.Y * C);
        Vector2 fociM = master;
        Vector2 fociA = slaveA;
        Vector2 fociB = slaveB;

        Vector2 bestPosition = new Vector2();
        double bestError = double.MaxValue;

        for(double x = 0; x < 1000; x += 1) {
            for(double y = 0; y < 1000; y += 1) {
                Vector2 testPosition = new Vector2((float)x, (float)y);
                double errorX = Math.Abs(Vector2.Distance(testPosition, fociM) - Vector2.Distance(testPosition, fociA) - distance.X);
                double errorY = Math.Abs(Vector2.Distance(testPosition, fociM) - Vector2.Distance(testPosition, fociB) - distance.Y);

                double totalError = errorX + errorY;

                if(totalError < bestError) {
                    bestError = totalError;
                    bestPosition = testPosition;
                }
            }
        }

        return bestPosition;
    }

    static void DrawHyperbola(Vector2 focus1, Vector2 focus2, float distanceDifference, Color color) {
        // The number of points to calculate along the hyperbola
        int numPoints = 500;

        // The range of x values to plot the hyperbola
        float minX = -500;
        float maxX = 1500;

        // Calculate the semi-major axis (a) and semi-minor axis (b) of the hyperbola
        float a = distanceDifference / 2;
        float c = Vector2.Distance(focus1, focus2) / 2;
        float b = (float)Math.Sqrt(c * c - a * a);

        // Calculate the center of the hyperbola
        Vector2 center = (focus1 + focus2) / 2;

        // Calculate the angle of rotation for the hyperbola
        float angle = (float)Math.Atan2(focus2.Y - focus1.Y, focus2.X - focus1.X);

        // Generate points along the hyperbola
        Vector2[] points = new Vector2[numPoints];
        float step = (maxX - minX) / (numPoints - 1);
        for(int i = 0; i < numPoints; i++) {
            float x = minX + i * step;
            float y = b * (float)Math.Sqrt((x * x) / (a * a) - 1);

            // Rotate and translate the point according to the hyperbola's orientation and position
            points[i] = RotatePoint(new Vector2(x, y), angle) + center;

            // Since hyperbola has two symmetric branches, we draw the negative side as well
            Vector2 negativePoint = RotatePoint(new Vector2(x, -y), angle) + center;
            if(i > 0) {
                // Connect the points with lines
                Raylib.DrawLineV(points[i - 1], points[i], color);
                Raylib.DrawLineV(points[i - 1], negativePoint, color);
            }
        }
    }

    // Helper method to rotate a point around the origin
    static Vector2 RotatePoint(Vector2 point, float angle) {
        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);
        return new Vector2(
            point.X * cos - point.Y * sin,
            point.X * sin + point.Y * cos
        );
    }

    static void Main() {
        int windowWidth = 1200;
        int windowHeight = 1000;
        Raylib.InitWindow(windowWidth, windowHeight, "LoranVisual");
        Raylib.SetExitKey(0);

        Camera2D camera = new Camera2D();
        camera.target = Vector2.Zero;
        camera.offset = new Vector2(windowWidth / 2, windowHeight / 2);
        camera.zoom = 1;

        int control = 1;
        Vector2 master = new Vector2(50, 250);
        Vector2 slaveA = new Vector2(100, 200);
        Vector2 slaveB = new Vector2(300, 100);
        Vector2 timeDifference = new Vector2(MicrosecondsToSeconds(50), MicrosecondsToSeconds(75));

        float speed = 10;
        bool runApp = true;
        while(runApp) {
            if(Raylib.GetMouseWheelMove() > 0) camera.zoom += 0.1f;
            if(Raylib.GetMouseWheelMove() < 0) camera.zoom -= 0.1f;
            if(camera.zoom > 2f) camera.zoom = 2f;
            if(camera.zoom < 0.1f) camera.zoom = 0.1f;

            if(Raylib.IsKeyDown(KeyboardKey.KEY_W)) camera.target.Y -= speed * 1 / camera.zoom;
            if(Raylib.IsKeyDown(KeyboardKey.KEY_S)) camera.target.Y += speed * 1 / camera.zoom;
            if(Raylib.IsKeyDown(KeyboardKey.KEY_A)) camera.target.X -= speed * 1 / camera.zoom;
            if(Raylib.IsKeyDown(KeyboardKey.KEY_D)) camera.target.X += speed * 1 / camera.zoom;

            if(Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE)) runApp = false;
            if(Raylib.IsKeyPressed(KeyboardKey.KEY_ONE)) control = 1;
            if(Raylib.IsKeyPressed(KeyboardKey.KEY_TWO)) control = 2;
            if(Raylib.IsKeyPressed(KeyboardKey.KEY_THREE)) control = 3;

            if(control == 1) slaveA = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
            if(control == 2) slaveB = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
            if(control == 3) {
                Vector2 td = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), camera);
                timeDifference = new Vector2(MicrosecondsToSeconds(td.X), MicrosecondsToSeconds(td.Y));
            }

            Vector2 receiver = CalculatePosition(master, slaveA, slaveB, timeDifference);

            Raylib.BeginDrawing();
            {
                Raylib.ClearBackground(Color.BLACK);
                Raylib.BeginMode2D(camera);
                {
                    Raylib.DrawLine(-int.MaxValue, 0, int.MaxValue, 0, Color.WHITE);
                    Raylib.DrawLine(0, -int.MaxValue, 0, int.MaxValue, Color.WHITE);

                    DrawHyperbola(master, slaveA, timeDifference.X * C, new Color(255, 0, 0, 100));
                    DrawHyperbola(master, slaveB, timeDifference.Y * C, new Color(0, 255, 0, 100));

                    Raylib.DrawCircleV(master, 8, Color.RED);
                    Raylib.DrawCircleV(slaveA, 8, Color.GREEN);
                    Raylib.DrawCircleV(slaveB, 8, Color.BLUE);
                    Raylib.DrawCircleV(receiver, 5, Color.WHITE);
                }
                Raylib.EndMode2D();

                Raylib.DrawText($"{receiver.X}, {receiver.Y}", 10, 10, 20, Color.WHITE);
                Raylib.DrawText($"{timeDifference.X}, {timeDifference.Y}", 10, 30, 20, Color.WHITE);
            }
            Raylib.EndDrawing();
            Thread.Sleep(1);
        }

        Raylib.CloseWindow();
    }
}
