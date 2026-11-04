using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Downbeat.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Downbeat.Content
{
    /// <summary>
    /// Loads content/content.json and its images with UnityWebRequest (same code path in the Editor and in Web). In a player the folder comes from <see cref="ContentLocator"/>: the one next to the
    /// program, else the last known good copy, else the built-in one. A folder that validates but then fails to load (a picture that does not decode) is passed over like one that does not validate.
    /// </summary>
    public sealed class ContentRepository : IContentRepository
    {
        const string ContentFile = "content.json";
        const string ReasonSeparator = " | ";
        /// <summary>A picture of a hand smaller than this on a side is not one (the placeholder Unity gives for a picture it cannot decode is 8 x 8).</summary>
        const int MinTutorialPicturePixels = 32;

        ContentSource _loadedSource;
        IFileProbe _loadedProbe;
        string _persistentDir;

        public IEnumerator Load(Action<ContentBundle> onLoaded, Action<string> onFailed)
        {
            if (Application.isEditor)
            {
                yield return LoadFrom(ContentLocator.BaseUrl(), onLoaded, onFailed);
                yield break;
            }
            yield return LoadFirstUsable(rejected => ContentLocator.Resolve(new DiskFileProbe(), rejected), new DiskFileProbe(), Application.persistentDataPath, onLoaded, onFailed);
        }

        /// <summary>
        /// Tries the copies of the content in the order <paramref name="resolve"/> gives them and uses the first that loads completely: its json, its pictures and every sound, which Unity must be able
        /// to open (a header that looks right is not enough). A copy that fails is passed over, with the reason kept. Nothing is kept as the last known good copy yet: that waits for
        /// <see cref="ConfirmLoaded"/>, when the app is ready with the content.
        /// </summary>
        public IEnumerator LoadFirstUsable(Func<ContentOrigin[], ContentSource> resolve, IFileProbe probe, string persistentDir, Action<ContentBundle> onLoaded, Action<string> onFailed)
        {
            var rejected = new List<ContentOrigin>();
            var failures = new List<string>();
            while (true)
            {
                var source = resolve(rejected.ToArray());
                ContentBundle bundle = null;
                string failure = null;
                yield return LoadFrom(ContentLocator.UrlOf(source.RootDir), b => bundle = b, e => failure = e);
                var reason = string.Join(ReasonSeparator, failures.Concat(new[] { source.Reason }).Where(r => r.Length > 0));
                if (bundle != null)
                {
                    var used = new ContentSource(source.RootDir, source.Origin, reason);
                    ContentLocator.SetActive(used);
                    _loadedSource = used;
                    _loadedProbe = probe;
                    _persistentDir = persistentDir;
                    LogChoice(used);
                    onLoaded(bundle);
                    yield break;
                }
                failures.Add($"{source.Origin} content failed to load: {failure}");
                if (source.Origin == ContentOrigin.BuiltIn)
                {
                    onFailed(string.Join(ReasonSeparator, failures.Concat(new[] { source.Reason }).Where(r => r.Length > 0)));
                    yield break;
                }
                rejected.Add(source.Origin);
            }
        }

        /// <summary>The app is ready with the content that was loaded (the sounds are in too): content from the folder next to the program is now kept as the last known good copy.</summary>
        public void ConfirmLoaded()
        {
            var source = _loadedSource;
            _loadedSource = null;
            if (source != null) ContentLocator.Promote(source, _persistentDir, _loadedProbe);
        }

        public IEnumerator Reload(Action<ContentBundle> onLoaded, Action<string> onFailed)
        {
            if (Application.isEditor)
            {
                yield return LoadFrom(ContentLocator.BaseUrl(), onLoaded, onFailed);
                yield break;
            }

            var probe = new DiskFileProbe();
            var folder = ContentLocator.ExternalFolder(ContentLocator.ExecutableDirectory());
            if (!probe.DirectoryExists(folder)) { onFailed($"external content: folder not found ({folder})"); yield break; }
            var errors = ContentFolderValidator.Validate(folder, probe);
            if (errors.Count > 0) { onFailed($"external content rejected ({folder}): {string.Join("; ", errors)}"); yield break; }

            ContentBundle bundle = null;
            string failure = null;
            yield return LoadFrom(ContentLocator.UrlOf(folder), b => bundle = b, e => failure = e);
            if (bundle == null) { onFailed($"external content failed to load: {failure}"); yield break; }
            _loadedSource = new ContentSource(folder, ContentOrigin.External, string.Empty);
            _loadedProbe = probe;
            _persistentDir = Application.persistentDataPath;
            onLoaded(bundle);
        }

        static void LogChoice(ContentSource source)
        {
            if (source.Origin == ContentOrigin.External) Debug.Log($"[content] using the content next to the program ({source.RootDir})");
            else Debug.LogWarning($"[content] using the {source.Origin} content ({source.RootDir}) because: {source.Reason}");
        }

        static IEnumerator LoadFrom(string baseUrl, Action<ContentBundle> onLoaded, Action<string> onFailed)
        {
            string json = null;
            string error = null;
            yield return FetchText(baseUrl + ContentFile, (text, e) => { json = text; error = e; });
            if (json == null) { onFailed($"content.json: {error}"); yield break; }
            json = TextBom.Strip(json);   // an editor that saves with a byte order mark must not make the content unreadable (the validator reads files the same way)

            ContentDto dto;
            try { dto = JsonUtility.FromJson<ContentDto>(json); }
            catch (ArgumentException e) { onFailed($"content.json is not valid JSON: {e.Message}"); yield break; }

            var problems = ContentValidator.Validate(dto);
            if (problems.Count > 0) { onFailed("content.json rejected: " + string.Join("; ", problems)); yield break; }

            var textures = new Dictionary<string, Texture2D>();
            foreach (var path in ImagePaths(dto.meadow))
            {
                Texture2D texture = null;
                yield return FetchTexture(baseUrl + path, (t, e) => { texture = t; error = e; });
                if (texture == null)
                {
                    foreach (var loaded in textures.Values) UnityEngine.Object.Destroy(loaded);
                    onFailed($"image {path}: {error}");
                    yield break;
                }
                textures[path] = texture;
            }
            // the pictures of the tutorial's hands are nice to have, not needed: one that cannot be loaded is reported and the tutorial draws that hand in code, it never makes a copy of the content unusable
            foreach (var path in TutorialImagePaths(dto.tutorial))
            {
                Texture2D texture = null;
                yield return FetchTexture(baseUrl + path, (t, e) => { texture = t; error = e; });
                if (texture != null && (texture.width < MinTutorialPicturePixels || texture.height < MinTutorialPicturePixels))
                {
                    // a picture that cannot be decoded can come back as the small placeholder of Unity (a red question mark), not as an error
                    error = $"it came back as a picture of {texture.width} x {texture.height}: not decoded";
                    UnityEngine.Object.Destroy(texture);
                    texture = null;
                }
                if (texture == null)
                {
                    Debug.LogWarning($"[content] the tutorial picture {path} could not be loaded ({error}); the tutorial draws that hand in code instead");
                    continue;
                }
                textures[path] = texture;
            }
            foreach (var file in ContentFolderValidator.SoundFiles(dto))
            {
                string problem = null;
                yield return TrialDecode(baseUrl + file, p => problem = p);
                if (problem == null) continue;
                foreach (var loaded in textures.Values) UnityEngine.Object.Destroy(loaded);
                onFailed($"sound {file}: {problem}");
                yield break;
            }
            onLoaded(new ContentBundle(dto, baseUrl, textures));
        }

        /// <summary>Opens a sound the way the sound engine will and throws the result away: a WAV whose header looks right can still be one that Unity cannot decode.</summary>
        static IEnumerator TrialDecode(string url, Action<string> done)
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { done($"{request.error} ({url})"); yield break; }
                AudioClip clip;
                try { clip = DownloadHandlerAudioClip.GetContent(request); }
                catch (Exception e) { done("Unity cannot decode it: " + e.Message); yield break; }
                if (clip == null) { done("Unity cannot decode it"); yield break; }
                var holdsSound = clip.samples > 0;
                UnityEngine.Object.Destroy(clip);
                done(holdsSound ? null : "it holds no samples");
            }
        }

        /// <summary>The pictures the meadow needs: one backdrop plate per sky stop (each file once).</summary>
        public static IEnumerable<string> ImagePaths(MeadowDto meadow)
        {
            var seen = new HashSet<string>();
            foreach (var stop in meadow.sky.stops)
            {
                if (seen.Add(stop.backdrop)) yield return stop.backdrop;
            }
        }

        /// <summary>The pictures of the hands of the tutorial (each file once; none for a path that is empty, which means the hand is drawn in code). A content file without the tutorial block has the default pictures.</summary>
        public static IEnumerable<string> TutorialImagePaths(TutorialDto tutorial)
        {
            var t = tutorial ?? new TutorialDto();
            if (!string.IsNullOrWhiteSpace(t.openHandImage)) yield return t.openHandImage;
            if (!string.IsNullOrWhiteSpace(t.fistImage) && t.fistImage != t.openHandImage) yield return t.fistImage;
        }

        static IEnumerator FetchText(string url, Action<string, string> done)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) done(request.downloadHandler.text, null);
                else done(null, $"{request.error} ({url})");
            }
        }

        static IEnumerator FetchTexture(string url, Action<Texture2D, string> done)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url, true))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { done(null, $"{request.error} ({url})"); yield break; }
                var texture = DownloadHandlerTexture.GetContent(request);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                done(texture, null);
            }
        }
    }
}
