using Silk.NET.Maths;

namespace _3D_Fractals.src
{
    public sealed class CameraDto
    {
        public float[] Pos { get; set; } = new float[3];
        public float Yaw { get; set; }
        public float Pitch { get; set; }

        public CameraDto() { }

        public CameraDto(Camera cam)
        {
            Pos = [cam.Pos.X, cam.Pos.Y, cam.Pos.Z];
            Yaw = cam.Yaw;
            Pitch = cam.Pitch;
        }
    }

    public class Camera
    {
        public Vector3D<float> Pos { get; set; } = new(0.0f, 0.0f, 0.0f);
        public Vector3D<float> Front { get; private set; } = new(0.0f, 0.0f, -1.0f);
        public Vector3D<float> Right { get; private set; }
        public Vector3D<float> Up { get; private set; } = Vector3D<float>.UnitY;

        public float Yaw { get; set; } = -90.0f;
        public float Pitch 
        { 
            get => _pitch;
            set
            {
                _pitch = Core.Clamp(value, -89.0f, 89.0f);
            }
        }
        private float _pitch = 0.0f;

        public void UpdateCameraVectors()
        {
            Vector3D<float> front;
            front.X = MathF.Cos(Core.ToRadians(Yaw)) * MathF.Cos(Core.ToRadians(Pitch));
            front.Y = MathF.Sin(Core.ToRadians(Pitch));
            front.Z = MathF.Sin(Core.ToRadians(Yaw)) * MathF.Cos(Core.ToRadians(Pitch));
            Front = Vector3D.Normalize(front);
            Right = Vector3D.Normalize(Vector3D.Cross(Front, Vector3D<float>.UnitY));
            Up = Vector3D.Normalize(Vector3D.Cross(Right, Front));
        }

        public void MoveForward(float delta) => Pos += Front * delta;
        public void MoveBackward(float delta) => Pos -= Front * delta;
        public void MoveRight(float delta) => Pos += Right * delta;
        public void MoveLeft(float delta) => Pos -= Right * delta;
        public void MoveUp(float delta) => Pos += Up * delta;
        public void MoveDown(float delta) => Pos -= Up * delta;

        public override string ToString()
        {
            return $"Camera:\n" +
                   $"   Position: {Pos}\n" +
                   $"   Camera Front: {Front}\n" +
                   $"   Camera Up: {Up}\n" +
                   $"   Camera Right: {Right}\n" +
                   $"   Yaw: {Yaw}\n" +
                   $"   Pitch: {Pitch}";
        }
    }
}
