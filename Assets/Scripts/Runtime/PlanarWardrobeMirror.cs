using UnityEngine;

namespace BuildYourFurniture
{
    // Planar mirror for the standard camera renderer. VR stereo needs per-eye render
    // targets and an XR-specific integration; this component does not claim that yet.
    [RequireComponent(typeof(Renderer))]
    public sealed class PlanarWardrobeMirror : MonoBehaviour
    {
        Camera reflectionCamera;
        RenderTexture reflectionTexture;
        Material mirrorMaterial;
        bool rendering;
        int lastFrame=-1;
        public void Initialize()
        {
            gameObject.layer=31;
            var shader=Shader.Find("Unlit/Texture");
            if(shader==null)shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null)shader=Shader.Find("Standard");
            mirrorMaterial=new Material(shader);
            GetComponent<Renderer>().material=mirrorMaterial;
        }
        void LateUpdate()
        {
            // URP does not reliably invoke OnWillRenderObject for this surface.
            RenderReflection(Camera.main);
        }
        void OnWillRenderObject()
        {
            RenderReflection(Camera.current);
        }
        void RenderReflection(Camera source)
        {
            if(rendering||source==null||source==reflectionCamera||!isActiveAndEnabled)return;
            if(source.cameraType==CameraType.Preview||source.cameraType==CameraType.Reflection)return;
            if(lastFrame==Time.frameCount)return;
            if(Vector3.Dot(source.transform.position-transform.position,transform.forward)<=0f)return;
            lastFrame=Time.frameCount;
            if(mirrorMaterial==null)Initialize();
            if(reflectionCamera==null)
            {
                var obj=new GameObject("Wardrobe planar reflection camera");
                obj.hideFlags=HideFlags.HideAndDontSave;
                reflectionCamera=obj.AddComponent<Camera>();
                reflectionCamera.enabled=false;
            }
            int targetWidth=Mathf.Clamp(source.pixelWidth/2,256,1024);
            int targetHeight=Mathf.Clamp(source.pixelHeight/2,256,1024);
            if(reflectionTexture==null||reflectionTexture.width!=targetWidth||reflectionTexture.height!=targetHeight)
            {
                if(reflectionTexture!=null){reflectionTexture.Release();Destroy(reflectionTexture);}
                reflectionTexture=new RenderTexture(targetWidth,targetHeight,16,RenderTextureFormat.ARGB32);
                reflectionTexture.name="Wardrobe live mirror";
                reflectionTexture.Create();
            }
            reflectionCamera.CopyFrom(source);
            reflectionCamera.enabled=false;
            reflectionCamera.targetTexture=reflectionTexture;
            reflectionCamera.cullingMask=source.cullingMask & ~(1<<31);
            reflectionCamera.stereoTargetEye=StereoTargetEyeMask.None;
            Vector3 normal=transform.forward;
            Vector3 origin=transform.position;
            Vector3 position=source.transform.position;
            Vector3 reflectedPosition=position-2f*Vector3.Dot(position-origin,normal)*normal;
            Vector3 forward=Vector3.Reflect(source.transform.forward,normal);
            Vector3 up=Vector3.Reflect(source.transform.up,normal);
            reflectionCamera.transform.SetPositionAndRotation(reflectedPosition,Quaternion.LookRotation(forward,up));
            reflectionCamera.projectionMatrix=source.projectionMatrix;
            reflectionCamera.clearFlags=CameraClearFlags.Skybox;
            reflectionCamera.depthTextureMode=DepthTextureMode.None;
            mirrorMaterial.mainTexture=reflectionTexture;
            if(mirrorMaterial.HasProperty("_BaseMap"))mirrorMaterial.SetTexture("_BaseMap",reflectionTexture);
            rendering=true;
            bool oldInvert=GL.invertCulling;
            try
            {
                GL.invertCulling=!oldInvert;
                reflectionCamera.Render();
            }
            finally
            {
                GL.invertCulling=oldInvert;
                rendering=false;
            }
        }
        void OnDestroy()
        {
            if(reflectionTexture!=null){reflectionTexture.Release();Destroy(reflectionTexture);}
            if(reflectionCamera!=null)Destroy(reflectionCamera.gameObject);
            if(mirrorMaterial!=null)Destroy(mirrorMaterial);
        }
    }
}