using UnityEngine;

namespace ResoMeshXParsing {
    public static class MeshXConverter {
        public static Mesh ConvertToUnityMesh(MeshXData meshXData) {
            if (meshXData == null || meshXData.Positions.Count == 0) {
                Debug.LogError("Invalid MeshXData");
                return null;
            }

            Mesh mesh = new Mesh();
            mesh.name = "MeshX_Import";

            if (meshXData.VertexCount > 65535) {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(meshXData.Positions);

            if (meshXData.HasNormals && meshXData.Normals.Count > 0) {
                mesh.SetNormals(meshXData.Normals);
            }

            if (meshXData.HasTangents && meshXData.Tangents.Count > 0) {
                mesh.SetTangents(meshXData.Tangents);
            }

            if (meshXData.HasColors && meshXData.Colors.Count > 0) {
                mesh.SetColors(meshXData.Colors);
            }

            SetUVChannels(mesh, meshXData);

            mesh.subMeshCount = meshXData.Submeshes.Count;
            for (int i = 0; i < meshXData.Submeshes.Count; i++) {
                MeshTopology type = ConvertTopology(meshXData.SubmeshTypes[i]);
                mesh.SetIndices(meshXData.Submeshes[i].ToArray(), type, i);
            }

            mesh.RecalculateBounds();

            if (!meshXData.HasNormals) {
                mesh.RecalculateNormals();
            }

            if (!meshXData.HasTangents && meshXData.UV2DChannels.Count > 0) {
                mesh.RecalculateTangents();
            }

            Debug.Log($"Successfully converted MeshX to Unity mesh: {meshXData.VertexCount} vertices, {meshXData.Submeshes.Count} submeshes");

            return mesh;
        }

        private static void SetUVChannels(Mesh mesh, MeshXData meshXData) {
            int channel2D = 0;
            int channel3D = 0;
            int channel4D = 0;

            for (int i = 0; i < meshXData.UVDimensions.Length; i++) {
                int dimension = meshXData.UVDimensions[i];

                if (dimension == 2 && channel2D < meshXData.UV2DChannels.Count) {
                    mesh.SetUVs(i, meshXData.UV2DChannels[channel2D]);
                    channel2D++;
                } else if (dimension == 3 && channel3D < meshXData.UV3DChannels.Count) {
                    mesh.SetUVs(i, meshXData.UV3DChannels[channel3D]);
                    channel3D++;
                } else if (dimension == 4 && channel4D < meshXData.UV4DChannels.Count) {
                    mesh.SetUVs(i, meshXData.UV4DChannels[channel4D]);
                    channel4D++;
                }
            }
        }

        private static MeshTopology ConvertTopology(SubmeshType type) {
            switch (type) {
                case SubmeshType.Quads:
                    return MeshTopology.Quads;
                case SubmeshType.Lines:
                    return MeshTopology.Lines;
                case SubmeshType.LineStrip:
                    return MeshTopology.LineStrip;
                case SubmeshType.Points:
                    return MeshTopology.Points;
                default:
                    return MeshTopology.Triangles;
            }
        }

        public static GameObject CreateGameObjectWithMesh(Mesh mesh, string name = "MeshX_Object") {
            if (mesh == null) {
                Debug.LogError("Cannot create GameObject - Mesh is null");
                return null;
            }

            GameObject obj = new GameObject(name);

            MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
            Material material = new Material(Shader.Find("Standard"));
            
            meshFilter.mesh = mesh;
            meshRenderer.material = material;
            material.color = Color.white;

            Debug.Log($"Created GameObject '{name}' with MeshRenderer");

            return obj;
        }
    }
}