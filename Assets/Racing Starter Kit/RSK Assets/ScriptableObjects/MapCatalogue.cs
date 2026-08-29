using System.Collections.Generic;
using UnityEngine;

namespace SpinMotion
{
    [System.Serializable]
    public class MapEntry
    {
        public string displayName = "Track";
        [Tooltip("Scene name exactly as it appears in Build Settings")]
        public string sceneName;
    }

    /// <summary>
    /// the track list the map selector reads.
    ///
    /// scenes are referenced by name rather than by asset, because SceneManager.LoadScene takes a
    /// name and because a scene asset reference would not tell us whether the scene is actually in
    /// Build Settings. a name that is missing from Build Settings fails loudly at load time, which is
    /// the right moment to find out.
    /// </summary>
    [CreateAssetMenu(fileName = "Map Catalogue", menuName = "Scriptable Objects/Map Catalogue")]
    public class MapCatalogue : ScriptableObject
    {
        public List<MapEntry> maps = new List<MapEntry>();

        public int Count { get { return maps.Count; } }

        public MapEntry Get(int index)
        {
            if (maps.Count == 0) return null;
            return maps[Mathf.Clamp(index, 0, maps.Count - 1)];
        }

        /// <summary>index of the entry matching a scene name, or -1</summary>
        public int IndexOfScene(string sceneName)
        {
            for (int i = 0; i < maps.Count; i++)
                if (maps[i] != null && maps[i].sceneName == sceneName) return i;
            return -1;
        }
    }
}
