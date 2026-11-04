using System.Collections.Generic;
using Downbeat.Core;
using UnityEngine;

namespace Downbeat.Content
{
    /// <summary>The parsed content.json plus every texture it refers to, keyed by the path used in the json.</summary>
    public sealed class ContentBundle
    {
        readonly Dictionary<string, Texture2D> _textures;

        public ContentBundle(ContentDto dto, string baseUrl, Dictionary<string, Texture2D> textures)
        {
            Dto = dto;
            BaseUrl = baseUrl;
            _textures = textures;
        }

        public ContentDto Dto { get; }
        /// <summary>Absolute URL of the content folder, ending in "/".</summary>
        public string BaseUrl { get; }

        public Texture2D Texture(string relativePath) => _textures[relativePath];

        /// <summary>The picture at that path if it was loaded (the pictures that are only nice to have, like the hands of the tutorial, may be missing); never throws.</summary>
        public bool TryTexture(string relativePath, out Texture2D texture)
        {
            texture = null;
            return !string.IsNullOrEmpty(relativePath) && _textures.TryGetValue(relativePath, out texture) && texture != null;
        }

        /// <summary>Gives the textures back. For a bundle that was replaced by a reload and is not drawn any more.</summary>
        public void Release()
        {
            foreach (var texture in _textures.Values)
            {
                if (texture == null) continue;
                if (Application.isPlaying) Object.Destroy(texture);
                else Object.DestroyImmediate(texture);
            }
            _textures.Clear();
        }
    }
}
