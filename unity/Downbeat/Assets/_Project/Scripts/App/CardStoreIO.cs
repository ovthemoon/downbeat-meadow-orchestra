using System;
using System.Collections.Generic;
using System.IO;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.App
{
    /// <summary>The cards as JSON (the stored form is <see cref="SessionCardDto"/>).</summary>
    public sealed class JsonCardCodec : ICardCodec
    {
        public string Encode(SessionCard card) => JsonUtility.ToJson(card.ToDto());

        public bool TryDecode(string json, out SessionCard card, out string problem)
        {
            card = null;
            SessionCardDto dto;
            try { dto = JsonUtility.FromJson<SessionCardDto>(json); }
            catch (ArgumentException e) { problem = "not valid JSON: " + e.Message; return false; }
            return SessionCard.TryFromDto(dto, out card, out problem);
        }
    }

    /// <summary>The real files for <see cref="CardStore"/>.</summary>
    public sealed class CardStoreIO : ICardFileSystem
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public bool FileExists(string path) => File.Exists(path);
        public void WriteAllText(string path, string text) => File.WriteAllText(path, text);
        public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
        public string ReadAllText(string path) => File.ReadAllText(path);

        public IReadOnlyList<string> GetFiles(string directory, string searchPattern)
        {
            var files = Directory.GetFiles(directory, searchPattern);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
    }
}
