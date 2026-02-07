using System.Collections.Generic;
using UnityEngine;

namespace ResoMeshXParsing {
    public enum ColorProfile {
        Linear,
        sRGB,
        HDR
    }

    public enum SubmeshType {
        Triangles,
        Quads,
        Lines,
        LineStrip,
        Points
    }

    public struct BoneBinding {
        public int boneIndex0;
        public int boneIndex1;
        public int boneIndex2;
        public int boneIndex3;
        public float weight0;
        public float weight1;
        public float weight2;
        public float weight3;

        public void Normalize() {
            float total = weight0 + weight1 + weight2 + weight3;
            if (total > 0) {
                weight0 /= total;
                weight1 /= total;
                weight2 /= total;
                weight3 /= total;
            }
        }
    }

    public struct BoneData {
        public string name;
        public Matrix4x4 bindPose;
    }

    public struct BlendShapeData {
        public string name;
        public List<BlendShapeFrame> frames;
    }

    public struct BlendShapeFrame {
        public float weight;
        public List<Vector3> deltaPositions;
        public List<Vector3> deltaNormals;
        public List<Vector3> deltaTangents;
    }

    public class MeshXData {
        public ulong VertexCount { get; set; }
        public ColorProfile Profile { get; set; }
        
        public bool HasNormals { get; set; }
        public bool HasTangents { get; set; }
        public bool HasColors { get; set; }
        public bool HasBones { get; set; }

        public List<Vector3> Positions { get; set; }
        public List<Vector3> Normals { get; set; }
        public List<Vector4> Tangents { get; set; }
        public List<Color> Colors { get; set; }
        public List<BoneBinding> BoneBindings { get; set; }

        public List<List<Vector2>> UV2DChannels { get; set; }
        public List<List<Vector3>> UV3DChannels { get; set; }
        public List<List<Vector4>> UV4DChannels { get; set; }
        public int[] UVDimensions { get; set; }

        public List<List<int>> Submeshes { get; set; }
        public List<SubmeshType> SubmeshTypes { get; set; }

        public List<BoneData> Bones { get; set; }
        public List<BlendShapeData> BlendShapes { get; set; }
        public int TriangleCount { get; set; }
        public int BoneCount { get; set; }
        public int SubmeshCount { get; set; }
        public uint FeatureFlags { get; set; }
        public int UVCount { get; set; }
        public string ColorProfile { get; set; } = "sRGB";
        public string EncodingName { get; set; }
        public int BlendShapeCount { get; set; }
        public int TriangleSubmeshCount { get; set; }
        public int Version { get; set; }

        public MeshXData() {
            Positions = new List<Vector3>();
            Normals = new List<Vector3>();
            Tangents = new List<Vector4>();
            Colors = new List<Color>();
            BoneBindings = new List<BoneBinding>();
            
            UV2DChannels = new List<List<Vector2>>();
            UV3DChannels = new List<List<Vector3>>();
            UV4DChannels = new List<List<Vector4>>();
            UVDimensions = new int[4];
            
            Submeshes = new List<List<int>>();
            SubmeshTypes = new List<SubmeshType>();
            
            Bones = new List<BoneData>();
            BlendShapes = new List<BlendShapeData>();
        }
    }
}