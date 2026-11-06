using System;
using System.Collections.Generic;
using System.IO;
using Unity.XR.XREAL;
using Unity.XR.XREAL.Samples;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace UnityAR
{
    /// <summary>
    /// Saves the room, meaning the environment mesh and the cubes placed in it, to one file per scan image, and
    /// puts saved rooms back.
    /// </summary>
    /// <remarks>
    /// Rooms are stored relative to the scan image's pose, so they can be placed again wherever the image is
    /// recognized. The live mesh comes from XREAL meshing, split by classification label by
    /// <see cref="MeshClassificationFracking"/>.
    /// </remarks>
    public class RoomMeshStore : MonoBehaviour
    {
        [Serializable]
        struct LabelMaterial
        {
            public NRMeshingVertexSemanticLabel label;
            public Material material;
        }

        const string k_FolderName = "RoomMeshes";
        const string k_FileExtension = ".roommesh";

        [SerializeField]
        ARMeshManager m_MeshManager;

        [SerializeField]
        MeshClassificationFracking m_Classification;

        [SerializeField]
        CubePlacer m_Cubes;

        [SerializeField]
        [Tooltip("Material for each classification label of a loaded mesh. Match the live classified mesh prefabs.")]
        List<LabelMaterial> m_LabelMaterials = new List<LabelMaterial>();

        [SerializeField]
        [Tooltip("Material for labels missing from the list above.")]
        Material m_DefaultMaterial;

        readonly List<GameObject> m_LoadedMeshes = new List<GameObject>();
        readonly Dictionary<MeshFilter, NRMeshingVertexSemanticLabel> m_LoadedLabels = new Dictionary<MeshFilter, NRMeshingVertexSemanticLabel>();
        readonly List<int> m_Triangles = new List<int>();
        readonly List<Vector3> m_Vertices = new List<Vector3>();
        readonly List<Vector3> m_Normals = new List<Vector3>();

        /// <summary>
        /// The folder saved room meshes are written to.
        /// </summary>
        public static string folderPath => Path.Combine(Application.persistentDataPath, k_FolderName);

        /// <summary>
        /// Whether the live mesh is being built and shown. False while a saved mesh is loaded.
        /// </summary>
        public bool isLiveScanning => m_MeshManager.enabled;

        /// <summary>
        /// Number of triangles in the current mesh: the live mesh while scanning, otherwise the loaded mesh.
        /// </summary>
        public int currentTriangleCount
        {
            get
            {
                var count = 0;
                foreach (var pair in GetCurrentMesh())
                {
                    if (pair.Key.sharedMesh.subMeshCount > 0)
                        count += (int)pair.Key.sharedMesh.GetIndexCount(0) / 3;
                }
                return count;
            }
        }

        /// <summary>
        /// Whether there is anything to save: a mesh or placed cubes.
        /// </summary>
        public bool hasContent => currentTriangleCount > 0 || m_Cubes.cubes.Count > 0;

        /// <summary>
        /// Whether a room has been saved under the key.
        /// </summary>
        public bool HasSavedMesh(string key) => File.Exists(GetPath(key));

        /// <summary>
        /// Saves the current mesh, the placed cubes and the world origin relative to a scan image, replacing
        /// anything saved under the key before.
        /// </summary>
        /// <remarks>
        /// The current mesh is the live mesh while scanning, otherwise the loaded mesh.
        /// </remarks>
        /// <param name="key">The key the room is saved under.</param>
        /// <param name="image">The scan image's world-space pose, which the room is stored relative to.</param>
        /// <returns>The saved room.</returns>
        public RoomMeshData Save(string key, Pose image)
        {
            var imageRotation = Quaternion.Inverse(image.rotation);
            var data = new RoomMeshData
            {
                key = key,
                savedAtUtc = DateTime.UtcNow,
                // The world origin is the identity pose in world space.
                originPose = new Pose(imageRotation * -image.position, imageRotation),
            };

            var worldToImage = Matrix4x4.TRS(image.position, image.rotation, Vector3.one).inverse;
            foreach (var pair in GetCurrentMesh())
            {
                var filter = pair.Key;
                var piece = ExtractPiece(filter.sharedMesh, worldToImage * filter.transform.localToWorldMatrix);
                if (piece == null)
                    continue;

                piece.label = (byte)pair.Value;
                data.pieces.Add(piece);
            }

            foreach (var cube in m_Cubes.cubes)
            {
                data.cubes.Add(new SavedCube
                {
                    pose = new Pose(worldToImage.MultiplyPoint3x4(cube.position), imageRotation * cube.rotation),
                    size = cube.localScale.x,
                });
            }

            RoomMeshFile.Write(GetPath(key), data);
            return data;
        }

        /// <summary>
        /// Loads the room saved under the key. Its mesh is shown in place of the live mesh, which stops scanning,
        /// and its cubes replace the placed cubes.
        /// </summary>
        /// <param name="key">The key the room was saved under.</param>
        /// <param name="parent">The transform the loaded mesh is attached to.</param>
        /// <param name="imageInParent">The scan image's pose in <paramref name="parent"/>'s space.</param>
        /// <returns>The loaded room, including the saved world origin relative to the scan image.</returns>
        public RoomMeshData Load(string key, Transform parent, Pose imageInParent)
        {
            var data = RoomMeshFile.Read(GetPath(key));
            ClearLoadedMeshes();
            var root = Spawn(data, parent, imageInParent).transform;
            m_LoadedMeshes.Add(root.gameObject);
            SetLiveScanning(false);

            // The mesh root sits at the scan image, so it converts the cubes' saved poses to world space.
            m_Cubes.Clear();
            foreach (var cube in data.cubes)
                m_Cubes.Place(new Pose(root.TransformPoint(cube.pose.position), root.rotation * cube.pose.rotation), cube.size);
            return data;
        }

        /// <summary>
        /// Removes any loaded mesh and resumes building the live mesh.
        /// </summary>
        public void ResumeLiveScanning()
        {
            ClearLoadedMeshes();
            SetLiveScanning(true);
        }

        void OnDestroy() => ClearLoadedMeshes();

        static string GetPath(string key)
        {
            var fileName = key;
            foreach (var invalid in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalid, '_');
            return Path.Combine(folderPath, fileName + k_FileExtension);
        }

        void SetLiveScanning(bool scanning)
        {
            m_MeshManager.enabled = scanning;

            // Hide rather than destroy the live mesh: XREAL keeps its meshing state across a stop and start,
            // and MeshClassificationFracking expects blocks it has seen to still exist when they update.
            foreach (var meshFilter in m_MeshManager.meshes)
            {
                if (meshFilter != null)
                    meshFilter.gameObject.SetActive(scanning);
            }
            foreach (var block in m_Classification.MeshFrackingMap.Values)
            {
                foreach (var filter in block.Values)
                {
                    if (filter != null)
                        filter.gameObject.SetActive(scanning);
                }
            }
        }

        // The mesh filters of the current mesh with their classification labels.
        IEnumerable<KeyValuePair<MeshFilter, NRMeshingVertexSemanticLabel>> GetCurrentMesh()
        {
            if (isLiveScanning)
            {
                foreach (var block in m_Classification.MeshFrackingMap.Values)
                {
                    foreach (var pair in block)
                    {
                        if (pair.Value != null && pair.Value.sharedMesh != null)
                            yield return new KeyValuePair<MeshFilter, NRMeshingVertexSemanticLabel>(pair.Value, pair.Key);
                    }
                }
            }
            else
            {
                foreach (var pair in m_LoadedLabels)
                {
                    if (pair.Key != null && pair.Key.sharedMesh != null)
                        yield return pair;
                }
            }
        }

        RoomMeshPiece ExtractPiece(Mesh mesh, Matrix4x4 meshToImage)
        {
            if (mesh.subMeshCount == 0)
                return null;

            mesh.GetTriangles(m_Triangles, 0);
            if (m_Triangles.Count == 0)
                return null;

            mesh.GetVertices(m_Vertices);
            mesh.GetNormals(m_Normals);
            var hasNormals = m_Normals.Count == m_Vertices.Count;

            // Each classified mesh shares its block's full vertex list; keep only the vertices its triangles use.
            var remap = new int[m_Vertices.Count];
            for (var i = 0; i < remap.Length; i++)
                remap[i] = -1;

            var vertices = new List<Vector3>();
            var normals = hasNormals ? new List<Vector3>() : null;
            var triangles = new int[m_Triangles.Count];
            for (var i = 0; i < m_Triangles.Count; i++)
            {
                var source = m_Triangles[i];
                if (remap[source] < 0)
                {
                    remap[source] = vertices.Count;
                    vertices.Add(meshToImage.MultiplyPoint3x4(m_Vertices[source]));
                    if (hasNormals)
                        normals.Add(meshToImage.MultiplyVector(m_Normals[source]).normalized);
                }
                triangles[i] = remap[source];
            }

            return new RoomMeshPiece
            {
                vertices = vertices.ToArray(),
                normals = normals?.ToArray(),
                triangles = triangles,
            };
        }

        GameObject Spawn(RoomMeshData data, Transform parent, Pose imageInParent)
        {
            // The parent is the trackables parent, under the XR Origin, so the mesh stays fixed in the room when the
            // world origin moves.
            var root = new GameObject("Saved Room Mesh");
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(imageInParent.position, imageInParent.rotation);

            foreach (var piece in data.pieces)
            {
                var label = (NRMeshingVertexSemanticLabel)piece.label;
                var mesh = new Mesh { name = label.ToString() };
                if (piece.vertices.Length > ushort.MaxValue)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(piece.vertices);
                mesh.SetTriangles(piece.triangles, 0);
                if (piece.normals != null)
                    mesh.SetNormals(piece.normals);
                else
                    mesh.RecalculateNormals();

                var pieceObject = new GameObject(mesh.name);
                pieceObject.transform.SetParent(root.transform, false);
                var filter = pieceObject.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                pieceObject.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(label);
                pieceObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                m_LoadedLabels[filter] = label;
            }
            return root;
        }

        Material GetMaterial(NRMeshingVertexSemanticLabel label)
        {
            foreach (var labelMaterial in m_LabelMaterials)
            {
                if (labelMaterial.label == label)
                    return labelMaterial.material;
            }
            return m_DefaultMaterial;
        }

        void ClearLoadedMeshes()
        {
            foreach (var root in m_LoadedMeshes)
            {
                if (root == null)
                    continue;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    Destroy(filter.sharedMesh);
                Destroy(root);
            }
            m_LoadedMeshes.Clear();
            m_LoadedLabels.Clear();
        }
    }
}
