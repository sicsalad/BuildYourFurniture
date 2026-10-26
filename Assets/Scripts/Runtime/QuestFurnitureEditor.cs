using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace BuildYourFurniture
{
    // Physical, opaque world-space menu; all interactions are XR raycasts, not IMGUI.
    public sealed class QuestFurnitureEditor : MonoBehaviour
    {
        public Func<RaycastHit,string> SelectFurniture;
        public Func<string> SelectedName;
        public Func<string,float> ReadNumber;
        public Action<string,float> WriteNumber;
        public Func<string> ReadTexture;
        public Action<string> WriteTexture;
        public Action Save;
        public Action Close;
        readonly List<GameObject> elements=new List<GameObject>();
        readonly Dictionary<Collider,Action> actions=new Dictionary<Collider,Action>();
        Transform panel;
        GameObject backdrop;
        InputDevice controller;
        bool previousTrigger,previousMenu;
        int texturePage;
        bool texturesVisible;
        string[] textureIds;
        static readonly string[] fields={"x","y","z","rotation","width","depth","height"};
        void Start()
        {
            var root=new GameObject("Quest furniture property panel");
            panel=root.transform;
            panel.SetParent(transform,false);
            panel.localPosition=new Vector3(0,-.15f,1.05f);
            panel.localRotation=Quaternion.identity;
            backdrop=GameObject.CreatePrimitive(PrimitiveType.Cube);
            backdrop.name="Opaque VR property background";
            backdrop.transform.SetParent(panel,false);
            backdrop.transform.localPosition=new Vector3(0,0,.018f);
            backdrop.transform.localScale=new Vector3(.79f,.82f,.025f);
            backdrop.GetComponent<Renderer>().material.color=new Color(.055f,.075f,.095f,1f);
            panel.gameObject.SetActive(false);
            controller=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            RefreshTextures();
        }
        void RefreshTextures()
        {
            var ids=new List<string>();
            ids.Add("");
            ids.AddRange(SurfaceTextureLibrary.Names.Select(n=>"procedural:"+n));
            ids.AddRange(Resources.LoadAll<Texture2D>("Textures").Select(t=>"Textures/"+t.name));
            textureIds=ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        void AddLabel(string value,float y,float x=-.35f,float size=.012f)
        {
            var go=new GameObject("Label");
            go.transform.SetParent(panel,false);
            go.transform.localPosition=new Vector3(x,y,-.065f);
            var label=go.AddComponent<TextMesh>();
            label.text=value;label.fontSize=48;label.characterSize=size;
            label.color=Color.white;
            elements.Add(go);
        }
        void AddButton(string caption,float x,float y,float width,Action action)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name="VR button "+caption;
            go.transform.SetParent(panel,false);
            go.transform.localPosition=new Vector3(x,y,-.055f);
            go.transform.localScale=new Vector3(width,.046f,.018f);
            go.GetComponent<Renderer>().material.color=new Color(.16f,.32f,.43f);
            actions[go.GetComponent<Collider>()]=action;
            elements.Add(go);
            var label=new GameObject("Caption");
            label.transform.SetParent(go.transform,false);
            label.transform.localPosition=new Vector3(0,0,-.56f);
            label.transform.localScale=new Vector3(1f/width,1f/.046f,1f);
            var text=label.AddComponent<TextMesh>();
            text.text=caption;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
            text.fontSize=36;text.characterSize=.007f;
            text.color=Color.white;
        }
        void Rebuild()
        {
            foreach(var element in elements)if(element!=null)Destroy(element);
            elements.Clear();actions.Clear();
            AddLabel("BUTOR TULAJDONSAGOK",.35f);
            AddLabel(SelectedName!=null?SelectedName():"Nincs kijeloles",.29f);
            if(SelectedName==null||string.IsNullOrEmpty(SelectedName()))
            {
                AddLabel("Mutass egy butorra es nyomd meg a ravaszt.",.15f);
            }
            else if(texturesVisible)
            {
                AddLabel("TEXTURAK",.22f);
                int start=texturePage*7;
                for(int i=start;i<Mathf.Min(start+7,textureIds.Length);i++)
                {
                    string id=textureIds[i];
                    string label=string.IsNullOrEmpty(id)?"Nincs textura":id.Replace("procedural:","").Replace("Textures/","");
                    if(label.Length>27)label=label.Substring(0,27);
                    int row=i-start;
                    AddButton(label,0,.16f-row*.066f,.68f,()=>{WriteTexture?.Invoke(id);texturesVisible=false;Rebuild();});
                }
                AddButton("<<",-.24f,-.32f,.18f,()=>{texturePage=Mathf.Max(0,texturePage-1);Rebuild();});
                AddButton(">>",.03f,-.32f,.18f,()=>{texturePage=Mathf.Min((textureIds.Length-1)/7,texturePage+1);Rebuild();});
                AddButton("Vissza",.26f,-.32f,.22f,()=>{texturesVisible=false;Rebuild();});
            }
            else
            {
                for(int i=0;i<fields.Length;i++)
                {
                    string key=fields[i];
                    float value=ReadNumber!=null?ReadNumber(key):0;
                    float y=.21f-i*.068f;
                    AddLabel(key+": "+value.ToString("0"),y,-.34f,.010f);
                    float step=key=="rotation"?5f:10f;
                    AddButton("-",.11f,y+.009f,.13f,()=>{WriteNumber?.Invoke(key,(ReadNumber!=null?ReadNumber(key):0)-step);Rebuild();});
                    AddButton("+",.29f,y+.009f,.13f,()=>{WriteNumber?.Invoke(key,(ReadNumber!=null?ReadNumber(key):0)+step);Rebuild();});
                }
                AddButton("Textura",-.19f,-.31f,.28f,()=>{texturesVisible=true;RefreshTextures();Rebuild();});
                AddButton("Mentes",.15f,-.31f,.24f,()=>{Save?.Invoke();Rebuild();});
            }
            AddButton("X",.32f,.35f,.1f,()=>{panel.gameObject.SetActive(false);Close?.Invoke();});
        }
        void Update()
        {
            if(!XRSettings.isDeviceActive||panel==null)return;
            if(!controller.isValid)controller=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool menu=false;
            controller.TryGetFeatureValue(CommonUsages.secondaryButton,out menu);
            if(menu&&!previousMenu)
            {
                panel.gameObject.SetActive(!panel.gameObject.activeSelf);
                if(panel.gameObject.activeSelf)Rebuild();
            }
            previousMenu=menu;
            bool trigger=false;
            controller.TryGetFeatureValue(CommonUsages.triggerButton,out trigger);
            if(trigger&&!previousTrigger)
            {
                Vector3 localPos;Quaternion localRot;
                if(controller.TryGetFeatureValue(CommonUsages.devicePosition,out localPos)&&
                   controller.TryGetFeatureValue(CommonUsages.deviceRotation,out localRot))
                {
                    var parent=transform.parent;
                    Vector3 start=parent!=null?parent.TransformPoint(localPos):localPos;
                    Vector3 direction=(parent!=null?parent.rotation:Quaternion.identity)*localRot*Vector3.forward;
                    RaycastHit hit;
                    if(Physics.Raycast(start,direction,out hit,12f))
                    {
                        Action click;
                        if(actions.TryGetValue(hit.collider,out click)&&panel.gameObject.activeSelf)
                            click();
                        else if(!panel.gameObject.activeSelf&&SelectFurniture!=null)
                        {
                            string selected=SelectFurniture(hit);
                            if(!string.IsNullOrEmpty(selected))
                            {texturesVisible=false;panel.gameObject.SetActive(true);Rebuild();}
                        }
                    }
                }
            }
            previousTrigger=trigger;
        }
        void OnDestroy()
        {
            if(panel!=null)Destroy(panel.gameObject);
        }
    }
}