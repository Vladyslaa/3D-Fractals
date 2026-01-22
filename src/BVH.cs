using Silk.NET.Maths;
using System.Runtime.InteropServices;

namespace _3D_Fractals.src
{
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct BVHNode
    {
        public Vector4D<float> Min;
        public Vector4D<float> Max;
    }

    internal class BVHBuilder
    {
        public List<BVHNode> Nodes = new();

        public int Build(GameObject[] objects)
        {
            Nodes.Clear();
            var indices = Enumerable.Range(0, objects.Length).ToList();
            return BuildRecursive(objects, indices);
        }

        private int BuildRecursive(GameObject[] objects, List<int> indices)
        {
            int nodeIdx = Nodes.Count;
            Nodes.Add(new BVHNode());

            var min = new Vector3D<float>(float.MaxValue);
            var max = new Vector3D<float>(float.MinValue);
            foreach (var i in indices)
            {
                var obj = objects[i];
                var localRad = obj.GetLocalRadius();

                float worldRX = Math.Abs(obj.RotationMatrix.M11) * localRad.X +
                                Math.Abs(obj.RotationMatrix.M21) * localRad.Y +
                                Math.Abs(obj.RotationMatrix.M31) * localRad.Z;

                float worldRY = Math.Abs(obj.RotationMatrix.M12) * localRad.X +
                                Math.Abs(obj.RotationMatrix.M22) * localRad.Y +
                                Math.Abs(obj.RotationMatrix.M32) * localRad.Z;

                float worldRZ = Math.Abs(obj.RotationMatrix.M13) * localRad.X +
                                Math.Abs(obj.RotationMatrix.M23) * localRad.Y +
                                Math.Abs(obj.RotationMatrix.M33) * localRad.Z;

                var worldRadius = new Vector3D<float>(worldRX, worldRY, worldRZ);

                min = Vector3D.Min(min, obj.Position - worldRadius);
                max = Vector3D.Max(max, obj.Position + worldRadius);
            }

            if (indices.Count <= 1)
            {
                Nodes[nodeIdx] = new BVHNode
                {
                    Min = new Vector4D<float>(min, -(indices[0] + 1)),
                    Max = new Vector4D<float>(max, -1),
                };
            }
            else
            {
                var size = max - min;
                if (size.X > size.Y && size.X > size.Z) indices.Sort((a, b) => objects[a].Position.X.CompareTo(objects[b].Position.X));
                else if (size.Y > size.Z) indices.Sort((a, b) => objects[a].Position.Y.CompareTo(objects[b].Position.Y));
                else indices.Sort((a, b) => objects[a].Position.Z.CompareTo(objects[b].Position.Z));

                int mid = indices.Count / 2;
                int left = BuildRecursive(objects, indices.GetRange(0, mid));
                int right = BuildRecursive(objects, indices.GetRange(mid, indices.Count - mid));

                Nodes[nodeIdx] = new BVHNode 
                {
                    Min = new Vector4D<float>(min, left),
                    Max = new Vector4D<float>(max, right)
                };
            }
            return nodeIdx;
        }
    }
}
