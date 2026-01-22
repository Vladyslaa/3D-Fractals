using Silk.NET.Maths;
using System.Runtime.InteropServices;

namespace _3D_Fractals.src
{
    public enum ObjectType : byte
    {
        Sphere = 0, 
        Box = 1,
        Torus = 2,
        Hexagon = 3,
        Mandelbulb = 4,
        MengerSponge = 5,
        SierpinskiTetrahedron = 6,
        Octahedron = 7,
        Cone = 8,
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct GameObjectData
    {
        [FieldOffset(0)]  public Vector4D<float> Type;
        [FieldOffset(16)] public Vector4D<float> Position;
        [FieldOffset(32)] public Vector4D<float> Scale;
        [FieldOffset(48)] public Vector4D<float> Color;
        [FieldOffset(64)] public Matrix4X4<float> RotationMatrix;
    }

    public sealed class GameObjectDto
    {
        public ObjectType Type { get; set; }
        public float[] Position { get; set; } = new float[3];
        public float[] Scale { get; set; } = new float[3];
        public float[] Rotation { get; set; } = new float[3];
        public float[] Color { get; set; } = new float[3];

        public GameObjectDto() { }

        public GameObjectDto(GameObject obj)
        {
            Type = (ObjectType)obj.Type;
            Position = [obj.Position.X, obj.Position.Y, obj.Position.Z];
            Scale = [obj.Scale.X, obj.Scale.Y, obj.Scale.Z];
            Rotation = [obj.Rotation.X, obj.Rotation.Y, obj.Rotation.Z];
            Color = [ obj.Color.X, obj.Color.Y, obj.Color.Z ]; 
        }
    }

    public class GameObject
    {
        public int Type
        {
            get => (int)_type;
            set 
            {
                _type = (ObjectType)value; 
                Raw.Type.X = (float)_type;
            }
        }
        private ObjectType _type;
        public Vector3D<float> Position 
        { 
            get => _position;
            set 
            { 
                _position = value;
                Raw.Position.X = value.X;
                Raw.Position.Y = value.Y;
                Raw.Position.Z = value.Z;
            }
        }
        private Vector3D<float> _position;
        public Vector3D<float> Scale 
        { 
            get => _scale;
            set 
            {
                _scale = value;
                Raw.Scale.X = value.X;
                Raw.Scale.Y = value.Y;
                Raw.Scale.Z = value.Z;
            } 
        }
        private Vector3D<float> _scale;
        public Vector3D<float> Rotation 
        { 
            get => _rotation;
            set
            {
                _rotation = value;
                UpdateRotation();
            } 
        }
        private Vector3D<float> _rotation;
        public Matrix4X4<float> RotationMatrix => _rotationMatrix;
        private Matrix4X4<float> _rotationMatrix;
        public Vector3D<float> Color 
        { 
            get => _color;
            set
            {
                _color = value;
                Raw.Color.X = MathF.Pow(value.X / 255f, 2.2f);
                Raw.Color.Y = MathF.Pow(value.Y / 255f, 2.2f);
                Raw.Color.Z = MathF.Pow(value.Z / 255f, 2.2f);
            }
        }
        private Vector3D<float> _color;

        public GameObjectData GetRawData() => Raw;
        private GameObjectData Raw;

        public GameObject(ObjectType type, Vector3D<float> position, Vector3D<float> scale, Vector3D<float> rotation, Vector3D<float> color)
        {
            Raw = new GameObjectData
            {
                Type = new Vector4D<float>(0.0f, 0.0f, 0.0f, 0.0f),
                Position = new Vector4D<float>(0.0f, 0.0f, 0.0f, 0.0f),
                Scale = new Vector4D<float>(0.0f, 0.0f, 0.0f, 0.0f),
                RotationMatrix = new Matrix4X4<float>(),
                Color = new Vector4D<float>(0.0f, 0.0f, 0.0f, 0.0f),
            };

            Type = (int)type;
            Position = position;
            Scale = scale;
            Rotation = rotation;
            Color = color;
        }

        public GameObject()
        {
            Raw = new GameObjectData
            {
                Type = new Vector4D<float>(0, 0, 0, 0),
                Position = new Vector4D<float>(0, 0, 0, 0),
                Scale = new Vector4D<float>(0, 0, 0, 0),
                RotationMatrix = new Matrix4X4<float>(),
                Color = new Vector4D<float>(0, 0, 0, 0)
            };
        }

        public GameObject(GameObjectDto dto) : this()
        {
            Type = (int)dto.Type;

            Position = new Vector3D<float>(dto.Position[0], dto.Position[1], dto.Position[2]);
            Scale = new Vector3D<float>(dto.Scale[0], dto.Scale[1], dto.Scale[2]);
            Rotation = new Vector3D<float>(dto.Rotation[0], dto.Rotation[1], dto.Rotation[2]);
            Color = new Vector3D<float>(dto.Color[0], dto.Color[1], dto.Color[2]);
        }

        public override string ToString()
        {
            return $"Object: {_type}\n" +
                   $"   Position: {Position}\n" +
                   $"   Scale: {Scale}\n" +
                   $"   Rotation: {Rotation}\n" +
                   $"   Color: {Color}\n" +
                   $"   Raw:\n" +
                   $"       Type: {Raw.Type}\n" +
                   $"       Position: {Raw.Position}\n" +
                   $"       Scale: {Raw.Scale}\n" +
                   $"       RotationMtx: {Raw.RotationMatrix}\n" +
                   $"       Color: {Raw.Color}\n";
        }

        public void Rotate(Vector3D<float> delta)
        {
            if (delta.X != 0) _rotation.X = (_rotation.X + delta.X) % 360f;
            if (delta.Y != 0) _rotation.Y = (_rotation.Y + delta.Y) % 360f;
            if (delta.Z != 0) _rotation.Z = (_rotation.Z + delta.Z) % 360f;

            UpdateRotation();
        }

        private void UpdateRotation()
        {
            float rx = Core.ToRadians(_rotation.X);
            float ry = Core.ToRadians(_rotation.Y);
            float rz = Core.ToRadians(_rotation.Z);

            var matX = Matrix4X4.CreateRotationX(rx);
            var matY = Matrix4X4.CreateRotationY(ry);
            var matZ = Matrix4X4.CreateRotationZ(rz);

            _rotationMatrix = matX * matY * matZ;

            Raw.RotationMatrix = Matrix4X4.Transpose(_rotationMatrix);
        }

        public Vector3D<float> GetLocalRadius()
        {
            switch ((ObjectType)Type)
            {
                case ObjectType.Sphere:
                case ObjectType.SierpinskiTetrahedron:
                case ObjectType.Octahedron:
                    return new Vector3D<float>(Scale.X);

                case ObjectType.Box:
                case ObjectType.MengerSponge:
                    return Scale;

                case ObjectType.Torus:
                    float bigR = Scale.X / 1.5f;
                    float smallR = Scale.Y / 3.0f;
                    return new Vector3D<float>(bigR + smallR, smallR, bigR + smallR);

                case ObjectType.Hexagon:
                    float hexRadius = Scale.X / 0.8660254f;
                    return new Vector3D<float>(hexRadius, hexRadius, Scale.Y);

                case ObjectType.Mandelbulb:
                    return new Vector3D<float>(Scale.X * 1.2f);

                case ObjectType.Cone:
                    return new Vector3D<float>(Scale.X * 1.73f, Scale.X, Scale.X * 1.73f);

                default:
                    return Scale;
            }
        }
    }
}