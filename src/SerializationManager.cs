using System.Text.Json;
using System.Text.Json.Serialization;

namespace _3D_Fractals.src
{
    internal static class SerializationManager
    {
        public sealed class PlayerDto
        {
            public CameraDto CamDto { get; set; } = new();
            public float MoveSpeed { get; set; } = 5f;
            public float Sensitivity { get; set; } = 0.1f;
            public bool ShowDebugInfo { get; set; } = true;
            public bool ShowGenerateMenu { get; set; } = true;
            public bool ShowSerealisationMenu { get; set; } = true;
            public bool ShowPlayerMenu { get; set; } = true;

            public PlayerDto() { }

            public PlayerDto
            (
                CameraDto camDto,
                float moveSpeed, float sensitivity,
                bool showDebugInfo, bool showGenerateMenu, bool showSerealisationMenu, bool showPlayerMenu
            )
            {
                CamDto = camDto;
                MoveSpeed = moveSpeed;
                Sensitivity = sensitivity;
                ShowDebugInfo = showDebugInfo;
                ShowGenerateMenu = showGenerateMenu;
                ShowSerealisationMenu = showSerealisationMenu;
                ShowPlayerMenu = showPlayerMenu;
            }
        }

        private static JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static void Initialise()
        {
            _options.Converters.Add(new JsonStringEnumConverter());
        }

        public static GameObject[] DeserialiseMap(string path)
        {
            string json = File.ReadAllText(path);
            GameObjectDto[]? dtos = JsonSerializer.Deserialize<GameObjectDto[]>(json, _options) ?? throw new JsonException("JSON is empty or invalid");
            return [.. dtos.Select(Validate)];
        }

        public static void SerialiseMap(string path)
        {
            var dtos = Core.Objects.Select(o => new GameObjectDto(o)) ?? throw new JsonException("JSON is empty or invalid");
            string json = JsonSerializer.Serialize(dtos, _options);
            File.WriteAllText(path, json);
        }

        public static void SerialiseAutoSave()
        {
            SerialiseMap("save/AutoSave.3D-FractalsMap");

            CameraDto camDto = new(Core.Camera);
            PlayerDto playerDto = new
            (
                camDto,
                Core.PlayerMoveSpeed, Core.MouseSensitivity,
                Core.ShowDebugInfo, Core.ShowGenerateMenu, Core.ShowSerealisationMenu, Core.ShowPlayerMenu
            );
            string json = JsonSerializer.Serialize(playerDto, _options);

            File.WriteAllText("save/AutoSave.3D-FractalsPlayer", json);
        }

        public static GameObject[] DeserialiseAutoSave()
        {
            string json = File.ReadAllText("save/AutoSave.3D-FractalsPlayer");
            PlayerDto? playerDto = JsonSerializer.Deserialize<PlayerDto>(json, _options) ?? throw new JsonException("JSON is empty or invalid");

            CameraDto camDto = playerDto.CamDto;
            Core.PlayerMoveSpeed = playerDto.MoveSpeed;
            Core.MouseSensitivity = playerDto.Sensitivity;

            Core.ShowDebugInfo = playerDto.ShowDebugInfo;
            Core.ShowGenerateMenu = playerDto.ShowGenerateMenu;
            Core.ShowSerealisationMenu = playerDto.ShowSerealisationMenu;
            Core.ShowPlayerMenu = playerDto.ShowPlayerMenu;

            Core.Camera.Pos = new Silk.NET.Maths.Vector3D<float>(camDto.Pos[0], camDto.Pos[1], camDto.Pos[2]);
            Core.Camera.Yaw = camDto.Yaw;
            Core.Camera.Pitch = camDto.Pitch;

            Core.Camera.UpdateCameraVectors();

            return DeserialiseMap("save/AutoSave.3D-FractalsMap");
        }

        private static GameObject Validate(GameObjectDto o)
        {
            if (o.Position.Length != 3) throw new JsonException("Position must have exactly 3 elements"); 
            if (o.Scale.Length != 3) throw new JsonException("Scale must have exactly 3 elements");
            if (o.Rotation.Length != 3) throw new JsonException("Rotation must have exactly 3 elements");
            if (o.Color.Length != 3) throw new JsonException("Color must have exactly 3 elements");

            return new GameObject(o);
        }
    }
}
