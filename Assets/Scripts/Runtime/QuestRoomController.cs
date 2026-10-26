using UnityEngine;
using UnityEngine.XR;

namespace BuildYourFurniture
{
    // Standalone Quest locomotion with tracked headset and Touch controllers.
    // Uses Unity XR input APIs, without any Meta SDK-specific runtime dependency.
    public sealed class QuestRoomController : MonoBehaviour
    {
        public Transform HeadCamera;
        public float MoveSpeed=1.4f;
        public float SnapAngle=30f;
        Transform origin;
        LineRenderer pointer;
        TextMesh hint;
        InputDevice head,left,right;
        bool turnReady=true,triggerHeld;
        public bool Active=>XRSettings.isDeviceActive;
        void Awake()
        {
            origin=new GameObject("Quest XR tracking origin").transform;
            origin.position=transform.position;
            origin.rotation=Quaternion.identity;
            transform.SetParent(origin,true);
            var line=new GameObject("Quest right controller ray");
            pointer=line.AddComponent<LineRenderer>();
            pointer.positionCount=2;
            pointer.startWidth=.008f;pointer.endWidth=.002f;
            pointer.useWorldSpace=true;
            pointer.material=new Material(Shader.Find("Sprites/Default"));
            pointer.startColor=Color.cyan;pointer.endColor=Color.cyan;
            var info=new GameObject("Quest help panel");
            hint=info.AddComponent<TextMesh>();
            hint.fontSize=44;hint.characterSize=.006f;
            hint.anchor=TextAnchor.MiddleCenter;hint.alignment=TextAlignment.Center;
            hint.color=Color.white;
            hint.text="BuildYourFurniture VR\nBal kar: mozgas | Jobb kar: fordulas\nJobb ravasz: kijeloles | B: menu";
        }
        void OnEnable(){RefreshDevices();}
        void RefreshDevices()
        {
            head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            left=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }
        void LateUpdate()
        {
            bool active=Active;
            pointer.enabled=active;
            if(hint!=null)hint.gameObject.SetActive(active);
            if(!active)return;
            if(!head.isValid||!left.isValid||!right.isValid)RefreshDevices();
            Vector3 headPos;Quaternion headRot;
            if(head.TryGetFeatureValue(CommonUsages.devicePosition,out headPos))
                transform.localPosition=headPos;
            if(head.TryGetFeatureValue(CommonUsages.deviceRotation,out headRot))
                transform.localRotation=headRot;
            Vector2 axis;
            if(left.TryGetFeatureValue(CommonUsages.primary2DAxis,out axis))
            {
                Vector3 forward=Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized;
                Vector3 rightDir=Vector3.ProjectOnPlane(transform.right,Vector3.up).normalized;
                origin.position+=(forward*axis.y+rightDir*axis.x)*MoveSpeed*Time.deltaTime;
            }
            if(right.TryGetFeatureValue(CommonUsages.primary2DAxis,out axis))
            {
                if(Mathf.Abs(axis.x)<.35f)turnReady=true;
                else if(turnReady&&Mathf.Abs(axis.x)>.75f)
                {
                    // Rotate around current head position, not the tracking origin.
                    origin.RotateAround(transform.position,Vector3.up,Mathf.Sign(axis.x)*SnapAngle);
                    turnReady=false;
                }
            }
            Vector3 localPosition;Quaternion localRotation;
            if(right.TryGetFeatureValue(CommonUsages.devicePosition,out localPosition)&&
               right.TryGetFeatureValue(CommonUsages.deviceRotation,out localRotation))
            {
                Vector3 start=origin.TransformPoint(localPosition);
                Vector3 direction=origin.rotation*localRotation*Vector3.forward;
                RaycastHit hit;
                bool found=Physics.Raycast(start,direction,out hit,12f);
                pointer.SetPosition(0,start);
                pointer.SetPosition(1,found?hit.point:start+direction*8f);
                bool trigger=false;
                right.TryGetFeatureValue(CommonUsages.triggerButton,out trigger);
                if(trigger&&!triggerHeld&&found)
                    hint.text="Kijelolt: "+hit.collider.gameObject.name+
                        "\nBal kar: mozgas | Jobb kar: fordulas";
                triggerHeld=trigger;
            }
            if(hint!=null)
            {
                hint.transform.position=transform.position+transform.forward*1.2f-transform.up*.35f;
                hint.transform.rotation=Quaternion.LookRotation(hint.transform.position-transform.position,Vector3.up);
            }
        }
        void OnDestroy()
        {
            if(pointer!=null)Destroy(pointer.gameObject);
            if(hint!=null)Destroy(hint.gameObject);
            if(origin!=null)Destroy(origin.gameObject);
        }
    }
}