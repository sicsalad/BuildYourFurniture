using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace BuildYourFurniture
{
    // Downloads user-specified public texture URLs during play; local Resources texture
    // remains the fallback when the image is unavailable or the application is offline.
    public sealed class RemoteSurfaceTexture : MonoBehaviour
    {
        Material targetMaterial;
        string imageUrl;
        public static void Attach(Material material,string url,Action<GameObject> register)
        {
            if(material==null||string.IsNullOrWhiteSpace(url))return;
            var go=new GameObject("Remote surface texture");
            var loader=go.AddComponent<RemoteSurfaceTexture>();
            loader.targetMaterial=material;loader.imageUrl=url;
            register(go);
        }
        IEnumerator Start()
        {
            using(var request=UnityWebRequestTexture.GetTexture(imageUrl))
            {
                yield return request.SendWebRequest();
                if(request.result!=UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("Could not download surface texture: "+request.error);
                    yield break;
                }
                if(targetMaterial==null)yield break;
                var tex=DownloadHandlerTexture.GetContent(request);
                targetMaterial.mainTexture=tex;
                if(targetMaterial.HasProperty("_BaseMap"))targetMaterial.SetTexture("_BaseMap",tex);
            }
        }
    }
}