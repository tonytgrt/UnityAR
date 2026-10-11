using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityAR
{
    /// <summary>
    /// The triangles of one classification label from one environment mesh block.
    /// </summary>
    public class RoomMeshPiece
    {
        /// <summary>The <c>NRMeshingVertexSemanticLabel</c> of these triangles.</summary>
        public byte label;

        /// <summary>Vertex positions, relative to the scan target the mesh was saved with.</summary>
        public Vector3[] vertices;

        /// <summary>Vertex normals in the same space as <see cref="vertices"/>, or null if the mesh had none.</summary>
        public Vector3[] normals;

        /// <summary>Triangle vertex indices.</summary>
        public int[] triangles;
    }

    /// <summary>
    /// A room mesh stored relative to a scan target, so it can be placed again wherever that target is
    /// recognized in a later session.
    /// </summary>
    public class RoomMeshData
    {
        /// <summary>The reference image name of the scan target the mesh belongs to.</summary>
        public string targetName;

        /// <summary>When the mesh was saved.</summary>
        public DateTime savedAtUtc;

        /// <summary>The mesh pieces.</summary>
        public readonly List<RoomMeshPiece> pieces = new List<RoomMeshPiece>();

        /// <summary>Total number of triangles in all pieces.</summary>
        public int triangleCount
        {
            get
            {
                var count = 0;
                foreach (var piece in pieces)
                    count += piece.triangles.Length / 3;
                return count;
            }
        }
    }

    /// <summary>
    /// Reads and writes <see cref="RoomMeshData"/> as a compact binary file.
    /// </summary>
    public static class RoomMeshFile
    {
        const uint k_Magic = 0x4D52_4155; // "UARM"
        const int k_Version = 1;

        /// <summary>
        /// Writes <paramref name="data"/> to <paramref name="path"/>, replacing any existing file.
        /// </summary>
        public static void Write(string path, RoomMeshData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Write to a temporary file first so an interrupted save never leaves a truncated mesh behind.
            var tempPath = path + ".tmp";
            using (var writer = new BinaryWriter(File.Create(tempPath)))
            {
                writer.Write(k_Magic);
                writer.Write(k_Version);
                writer.Write(data.targetName ?? string.Empty);
                writer.Write(data.savedAtUtc.ToBinary());
                writer.Write(data.pieces.Count);
                foreach (var piece in data.pieces)
                {
                    writer.Write(piece.label);
                    WriteVectors(writer, piece.vertices);
                    var hasNormals = piece.normals != null && piece.normals.Length == piece.vertices.Length;
                    writer.Write(hasNormals);
                    if (hasNormals)
                        WriteVectors(writer, piece.normals);
                    writer.Write(piece.triangles.Length);
                    foreach (var index in piece.triangles)
                        writer.Write(index);
                }
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }

        /// <summary>
        /// Reads a room mesh written by <see cref="Write"/>.
        /// </summary>
        /// <exception cref="InvalidDataException">The file is not a room mesh file or has an unsupported version.</exception>
        public static RoomMeshData Read(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadUInt32() != k_Magic)
                throw new InvalidDataException($"{path} is not a room mesh file.");
            var version = reader.ReadInt32();
            if (version != k_Version)
                throw new InvalidDataException($"{path} has unsupported room mesh version {version}.");

            var data = new RoomMeshData
            {
                targetName = reader.ReadString(),
                savedAtUtc = DateTime.FromBinary(reader.ReadInt64()),
            };
            var pieceCount = reader.ReadInt32();
            for (var i = 0; i < pieceCount; i++)
            {
                var piece = new RoomMeshPiece { label = reader.ReadByte(), vertices = ReadVectors(reader) };
                if (reader.ReadBoolean())
                    piece.normals = ReadVectors(reader);
                piece.triangles = new int[reader.ReadInt32()];
                for (var t = 0; t < piece.triangles.Length; t++)
                    piece.triangles[t] = reader.ReadInt32();
                data.pieces.Add(piece);
            }
            return data;
        }

        static void WriteVectors(BinaryWriter writer, Vector3[] vectors)
        {
            writer.Write(vectors.Length);
            foreach (var v in vectors)
            {
                writer.Write(v.x);
                writer.Write(v.y);
                writer.Write(v.z);
            }
        }

        static Vector3[] ReadVectors(BinaryReader reader)
        {
            var vectors = new Vector3[reader.ReadInt32()];
            for (var i = 0; i < vectors.Length; i++)
                vectors[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            return vectors;
        }
    }
}
