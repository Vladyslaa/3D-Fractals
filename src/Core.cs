using Silk.NET.Input;
using Silk.NET.Maths;
using System.Numerics;

namespace _3D_Fractals.src
{
    internal static class Core
    {
        public static string AppName { get; } = "3D-Fractals";

        public static uint WindowWidth { get; set; } = 1920;
        public static uint WindowHeight { get; set; } = 1080;

        private static IKeyboard _keyboard;

        private static IMouse _mouse;
        private static bool _mouseVisible = false;
        private static float _mouseLastX = WindowWidth / 2.0f;
        private static float _mouseLastY = WindowHeight / 2.0f;

        public static Vector3D<float> GlobalLightDir { get; private set; }
        private static Vector3D<float> _globalLightDirection = new(0.0f, 45.0f, 0.0f);
        private static Vector3D<float> _globalLightDirectionBase = Vector3D.Normalize(new Vector3D<float>(1.0f, 1.0f, 1.0f));

        public static Camera Camera { get => _camera; }
        private static Camera _camera = new();

        public static readonly ulong Mb = 1024 * 1024;
        public static readonly ulong Gb = 1024 * 1024 * 1024;

        private static Random _random = new();

        public static bool ShowDebugInfo = true;

        public static bool ShowSerealisationMenu = true;
        public static uint PathBufferSize = 256;
        public static byte[] SavePathBuffer = new byte[PathBufferSize];
        public static byte[] LoadPathBuffer = new byte[PathBufferSize];

        public static bool ShowGenerateMenu = true;
        public static bool RotateAllObjects = false;
        public static Vector3 RotationSpeed = new(0.0f, 30.0f, 0.0f);
        public static int GenerateWidth = 10;
        public static int GenerateHeight = 10;
        public static Vector3 GenerateScaleMin = new(0.5f, 0.5f, 0.5f);
        public static Vector3 GenerateScaleMax = new(2.0f, 2.0f, 2.0f);
        public static Vector3 GenerateRotationDelta = new(360.0f, 360.0f, 360.0f);
        public static bool GenerateGradient = true;
        public static Vector3 GenerateColorMin = new(0.0f, 0.0f, 0.0f);
        public static Vector3 GenerateColorMax = new(255.0f, 255.0f, 255.0f);

        public static bool ShowPlayerMenu = true;
        public static float PlayerMoveSpeed = 5.0f;
        public static float MouseSensitivity = 0.1f;

        public static GameObject[] Objects { get; set; }
        public static int MaxObjects { get; } = 10240;

        public static float ToRadians(float angle) => angle * (MathF.PI / 180.0f);
        public static float Clamp(float value, float min, float max) => MathF.Min(MathF.Max(value, min), max);

        public static void UpdateWindowSize(uint width, uint height)
        {
            WindowWidth = width;
            WindowHeight = height;
        }

        public static float NextFloat(float min, float max)
        {
            return (float)(_random.NextDouble() * (max - min) + min);
        }

        public static void Initialize(IInputContext input)
        {
            try
            {
                Objects = SerializationManager.DeserialiseAutoSave();
            }
            catch (Exception)
            {
                Console.WriteLine($"[WARNING] Failed to load the last autosave\nLoading default map.");
                try
                {
                    Objects = SerializationManager.DeserialiseMap("maps/Default.json");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[WARNING] Failed to load the default map file \"maps/Default.json\": {e}\nGenerating default objects.");
                    GenerateObjects();
                }
            }

            _keyboard = input.Keyboards[0];
            _keyboard.KeyDown += OnKeyDown;

            _mouse = input.Mice[0];
            HideCursor();
        }

        public static void Update(float deltaTime)
        {
            Rotate(ref _globalLightDirection, new Vector3D<float>(0.0f, 30.0f * deltaTime, 0.0f));
            CalcGlobalLightDir();

            if (RotateAllObjects)
            {
                foreach (var obj in Objects)
                {
                    obj.Rotate(new Vector3D<float>
                    (
                        RotationSpeed.X * deltaTime,
                        RotationSpeed.Y * deltaTime,
                        RotationSpeed.Z * deltaTime
                    ));
                }
            }

            if (_keyboard.IsKeyPressed(Key.W))
            {
                _camera.MoveForward(deltaTime * PlayerMoveSpeed);
            }
            if (_keyboard.IsKeyPressed(Key.A))
            {
                _camera.MoveLeft(deltaTime * PlayerMoveSpeed);
            }
            if (_keyboard.IsKeyPressed(Key.S))
            {
                _camera.MoveBackward(deltaTime * PlayerMoveSpeed);
            }
            if (_keyboard.IsKeyPressed(Key.D))
            {
                _camera.MoveRight(deltaTime * PlayerMoveSpeed);
            }
            if (_keyboard.IsKeyPressed(Key.ControlLeft) || _keyboard.IsKeyPressed(Key.Q))
            {
                _camera.MoveDown(deltaTime * PlayerMoveSpeed);
            }
            if (_keyboard.IsKeyPressed(Key.Space) || _keyboard.IsKeyPressed(Key.E))
            {
                _camera.MoveUp(deltaTime * PlayerMoveSpeed);
            }
            if (_keyboard.IsKeyPressed(Key.Escape))
            {
                Program.Exit();
            }

            if (!_mouseVisible)
            {
                var pos = _mouse.Position;
                float xOffset = (float)(pos.X - _mouseLastX) * MouseSensitivity;
                float yOffset = (float)(_mouseLastY - pos.Y) * MouseSensitivity;
                _mouseLastX = pos.X;
                _mouseLastY = pos.Y;

                _camera.Yaw += xOffset;
                _camera.Pitch += yOffset;

                _camera.UpdateCameraVectors();
            }
        }

        private static void HideCursor()
        {
            _mouseVisible = false;
            _mouse.Cursor.CursorMode = CursorMode.Raw;
            _mouse.Cursor.IsConfined = true;
        }

        private static void ShowCursor()
        {
            _mouseVisible = true;
            _mouse.Cursor.CursorMode = CursorMode.Normal;
            _mouse.Cursor.IsConfined = false;
        }

        public static void Rotate(ref Vector3D<float> angles, Vector3D<float> delta)
        {
            if (delta.X != 0) angles.X += delta.X;
            if (delta.Y != 0) angles.Y += delta.Y;
            if (delta.Z != 0) angles.Z += delta.Z;

            angles.X %= 360f;
            angles.Y %= 360f;
            angles.Z %= 360f;
        }

        private static void CalcGlobalLightDir()
        {
            float rx = ToRadians(_globalLightDirection.X);
            float ry = ToRadians(_globalLightDirection.Y);
            float rz = ToRadians(_globalLightDirection.Z);

            var rotX = Matrix4X4.CreateRotationX(rx);
            var rotY = Matrix4X4.CreateRotationY(ry);
            var rotZ = Matrix4X4.CreateRotationZ(rz);

            var rotMatrix = rotX * rotY * rotZ;

            var transformed = Vector3D.Transform(_globalLightDirectionBase, rotMatrix);
            GlobalLightDir = Vector3D.Normalize(transformed);
        }

        public static void GenerateObjects()
        {
            GenerateWidth = Math.Max(1, GenerateWidth);
            GenerateHeight = Math.Max(1, GenerateHeight);

            if (GenerateWidth * GenerateHeight > MaxObjects)
            {
                GenerateWidth = MaxObjects / GenerateHeight;
            }

            Vector3D<float> GradientColorA = new(), GradientColorB = new();
            if (GenerateGradient)
            {
                GradientColorA = new Vector3D<float>(NextFloat(0, 255), NextFloat(0, 255), NextFloat(0, 255)); 
                GradientColorB = new Vector3D<float>(NextFloat(0, 255), NextFloat(0, 255), NextFloat(0, 255));
            }

            Objects = new GameObject[GenerateWidth * GenerateHeight];
            for (int j = 0; j < GenerateHeight; j++)
            {
                for (int i = 0; i < GenerateWidth; i++)
                {
                    float R, G, B;
                    if (!GenerateGradient)
                    {
                        R = NextFloat(GenerateColorMin.X, GenerateColorMax.X);
                        G = NextFloat(GenerateColorMin.Y, GenerateColorMax.Y);
                        B = NextFloat(GenerateColorMin.Z, GenerateColorMax.Z);
                    }
                    else
                    {
                        float tx = i / (float)(GenerateWidth - 1);
                        float ty = j / (float)(GenerateHeight - 1);

                        float t = (tx + ty) * 0.5f;

                        R = GradientColorA.X + (GradientColorB.X - GradientColorA.X) * t;
                        G = GradientColorA.Y + (GradientColorB.Y - GradientColorA.Y) * t;
                        B = GradientColorA.Z + (GradientColorB.Z - GradientColorA.Z) * t;

                    }

                    Objects[j * GenerateWidth + i] = new GameObject((ObjectType)_random.Next(9),
                        new Vector3D<float>(i * 4, 0.0f, j * 4),
                        new Vector3D<float>
                        (
                            NextFloat(GenerateScaleMin.X, GenerateScaleMax.X),
                            NextFloat(GenerateScaleMin.Y, GenerateScaleMax.Y),
                            NextFloat(GenerateScaleMin.Z, GenerateScaleMax.Z)
                        ),
                        new Vector3D<float>
                        (
                            NextFloat(-GenerateRotationDelta.X, GenerateRotationDelta.X),
                            NextFloat(-GenerateRotationDelta.Y, GenerateRotationDelta.Y),
                            NextFloat(-GenerateRotationDelta.Z, GenerateRotationDelta.Z)
                        ),
                        new Vector3D<float>(R, G, B)
                    );
                }
            }
        }

        private static void OnKeyDown(IKeyboard keyboard, Key key, int code)
        {
            switch (key)
            {
                case Key.F1: 
                    ShowDebugInfo = !ShowDebugInfo;
                    break;
                case Key.F2: 
                    ShowGenerateMenu = !ShowGenerateMenu;
                    break;
                case Key.F3:
                    ShowSerealisationMenu = !ShowSerealisationMenu;
                    break;
                case Key.F4:
                    ShowPlayerMenu = !ShowPlayerMenu;
                    break;
                case Key.AltLeft: 
                    _mouseVisible = !_mouseVisible; 
                    if (_mouseVisible) ShowCursor(); 
                    else HideCursor(); 
                    break;
            }
        }
    }
}
