using System;
using System.Collections.Generic;
using System.IO;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Content
{
    /// <summary>
    /// Is this folder usable as the content of the app? content.json must read, its numbers must be in range and its ids unique (<see cref="ContentValidator"/>), and every picture and sound it
    /// refers to must be there and look whole (<see cref="MediaFileCheck"/>). All problems are reported, not just the first, each with the file or the setting it is about.
    /// </summary>
    public static class ContentFolderValidator
    {
        public const string ContentFile = "content.json";

        public static IReadOnlyList<string> Validate(string root, IFileProbe probe)
        {
            var errors = new List<string>();
            var jsonPath = Path.Combine(root, ContentFile);
            if (!probe.FileExists(jsonPath)) { errors.Add(ContentFile + " is missing"); return errors; }

            string text;
            try { text = probe.ReadAllText(jsonPath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { errors.Add($"{ContentFile} cannot be read: {e.Message}"); return errors; }
            if (string.IsNullOrWhiteSpace(text)) { errors.Add(ContentFile + " is empty"); return errors; }

            ContentDto dto;
            try { dto = JsonUtility.FromJson<ContentDto>(text); }
            catch (ArgumentException e) { errors.Add($"{ContentFile} is not valid JSON: {e.Message}"); return errors; }
            if (dto == null) { errors.Add(ContentFile + " holds no content"); return errors; }

            errors.AddRange(ContentValidator.Validate(dto));
            CheckFiles(root, dto, probe, errors);
            return errors;
        }

        /// <summary>The sounds and pictures the content refers to (each file once), as the relative paths written in content.json.</summary>
        public static IEnumerable<string> SoundFiles(ContentDto dto)
        {
            var seen = new HashSet<string>();
            if (dto.instruments != null)
            {
                foreach (var instrument in dto.instruments)
                {
                    if (instrument?.samples == null) continue;
                    foreach (var sample in instrument.samples)
                    {
                        if (!string.IsNullOrWhiteSpace(sample?.file) && seen.Add(sample.file)) yield return sample.file;
                    }
                }
            }
            if (dto.nature?.instruments == null) yield break;
            foreach (var instrument in dto.nature.instruments)
            {
                if (instrument?.samples == null) continue;
                foreach (var sample in instrument.samples)
                {
                    if (!string.IsNullOrWhiteSpace(sample?.file) && seen.Add(sample.file)) yield return sample.file;
                }
            }
        }

        static void CheckFiles(string root, ContentDto dto, IFileProbe probe, List<string> errors)
        {
            foreach (var relative in SoundFiles(dto))
            {
                var problem = Problem(root, relative, probe, (path, length, read) => MediaFileCheck.CheckWav(length, read));
                if (problem != null) errors.Add(problem);
            }
            if (dto.meadow?.sky?.stops != null)
            {
                foreach (var relative in ContentRepository.ImagePaths(dto.meadow)) CheckImage(root, relative, probe, errors);
            }
            // a folder that names pictures for the hands of the tutorial must have them whole (an empty path, which draws the hands in code, needs none)
            foreach (var relative in ContentRepository.TutorialImagePaths(dto.tutorial))
            {
                if (TutorialContentValidator.IsPlainImagePath(relative)) CheckImage(root, relative, probe, errors);   // a path that is not plain is reported by the content validator; it is not looked for on the disk
            }
        }

        static void CheckImage(string root, string relative, IFileProbe probe, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(relative)) return;
            var problem = Problem(root, relative, probe, MediaFileCheck.CheckImage);
            if (problem != null) errors.Add(problem);
        }

        static string Problem(string root, string relative, IFileProbe probe, Func<string, long, Func<long, int, byte[]>, string> check)
        {
            var path = Path.Combine(root, relative);
            if (!probe.FileExists(path)) return $"{relative} is missing";
            try
            {
                var length = probe.FileLength(path);
                var reason = check(relative, length, (offset, count) => probe.ReadBytes(path, offset, count));
                return reason == null ? null : $"{relative}: {reason}";
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return $"{relative} cannot be read: {e.Message}";
            }
        }
    }
}
