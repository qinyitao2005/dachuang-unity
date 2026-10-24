using UnityEngine;
using UnityEngine.UI;

namespace RoboticsLab
{
    /// <summary>Draws program data inside the imported pendant's own LCD.</summary>
    public sealed class LabPendantScreenView : MonoBehaviour
    {
        public Text Header { get; private set; }
        public Text ProgramRows { get; private set; }
        public Text Status { get; private set; }
        public Text Coordinates { get; private set; }
        public ScrollRect Scroll { get; private set; }
        RectTransform content;
        LabPendantProgramModel model;
        int activeLine=-1;
        LabTrajectoryState executionState=LabTrajectoryState.Ready;
        const float Resolution=2;

        public void Initialize(RectTransform screen, Font font)
        {
            screen.SetAsLastSibling();
            if(!screen.GetComponent<RectMask2D>())screen.gameObject.AddComponent<RectMask2D>();
            screen.GetComponent<Image>().color = new Color(.91f,.94f,.97f,1);
            // Generate larger font glyphs, then sample them down to the authored LCD size.
            // This keeps the small pendant text readable at the 1.5x housing scale.
            var surface=new GameObject("LCDContent",typeof(RectTransform)).GetComponent<RectTransform>();
            surface.SetParent(screen,false);surface.anchorMin=surface.anchorMax=surface.pivot=new Vector2(0,1);
            surface.sizeDelta=screen.rect.size*Resolution;surface.localScale=Vector3.one/Resolution;
            Header = Label(surface,"ProgramHeader",font,8,new Color(.08f,.18f,.28f),new Vector2(3,-2),new Vector2(180,12));
            Header.text = "TEACH  /  PROGRAM";
            var viewport = Rect(surface,"ProgramViewport",new Vector2(3,-17),new Vector2(180,83));
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect(viewport,"Content",Vector2.zero,new Vector2(180,83));
            ProgramRows = content.gameObject.AddComponent<Text>();
            ProgramRows.font=font;ProgramRows.fontSize=20;ProgramRows.lineSpacing=1.1f;
            ProgramRows.color=new Color(.06f,.10f,.16f);ProgramRows.alignment=TextAnchor.UpperLeft;
            ProgramRows.horizontalOverflow=HorizontalWrapMode.Overflow;ProgramRows.verticalOverflow=VerticalWrapMode.Overflow;
            ProgramRows.raycastTarget=false;
            Scroll = viewport.gameObject.AddComponent<ScrollRect>();
            Scroll.viewport=viewport;Scroll.content=content;Scroll.horizontal=false;
            Scroll.vertical=true;Scroll.movementType=ScrollRect.MovementType.Clamped;Scroll.scrollSensitivity=24;
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=new Color(0,0,0,.001f);
            Coordinates=Label(surface,"PointCoordinates",font,7,new Color(.18f,.27f,.36f),new Vector2(3,-101),new Vector2(180,10));
            Status=Label(surface,"RecordingStatus",font,8,new Color(.05f,.25f,.45f),new Vector2(3,-111),new Vector2(180,14));
            var strip=Rect(surface,"FunctionStrip",new Vector2(0,-125),new Vector2(186,13));
            var stripImage=strip.gameObject.AddComponent<Image>();stripImage.color=new Color(.13f,.21f,.29f);stripImage.raycastTarget=false;
            string[] captions={"L直线","C圆弧","撤销","速度","清空"};
            for(int i=0;i<captions.Length;i++)
            {
                var key=Label(surface,"FunctionF"+(i+1),font,8,Color.white,new Vector2(32.9f+i*23.8f,-125),new Vector2(24,13));
                key.alignment=TextAnchor.MiddleCenter;key.text=captions[i];
            }
        }

        public void Bind(LabPendantProgramModel document)
        {
            if(model!=null)model.Changed-=Refresh;
            model=document;if(model!=null)model.Changed+=Refresh;Refresh();
        }
        public void Refresh()
        {
            if(!ProgramRows||model==null)return;
            var rows=model.GetRows();
            if(activeLine>=0&&activeLine<rows.Length&&(executionState==LabTrajectoryState.Running||executionState==LabTrajectoryState.Paused||executionState==LabTrajectoryState.Faulted))
                rows[activeLine]="<color=#005D94><b>"+rows[activeLine]+"</b></color>";
            ProgramRows.text=string.Join("\n",rows)+"\n[End]";
            content.sizeDelta=new Vector2(180,Mathf.Max(83,(model.Instructions.Count+1)*13))*Resolution;
            Canvas.ForceUpdateCanvases();Scroll.verticalNormalizedPosition=0;
            Status.text=model.HasPendingArc?"C：中间点已记录，请记录终点":model.Instructions.Count==0?"F1直线 / F2圆弧 / ENTER记录":"已记录 "+model.Instructions.Count+" 行 / "+model.Points.Count+" 点";
        }
        public void SetStatus(string text){if(Status)Status.text=text.Length>22?text.Substring(0,21)+"…":text;}
        public void SetExecution(int line,LabTrajectoryState state,string message)
        {
            activeLine=line;executionState=state;Refresh();
            if(model!=null&&model.Instructions.Count>5&&state!=LabTrajectoryState.Ready)
                Scroll.verticalNormalizedPosition=1-Mathf.Clamp01((line-2f)/Mathf.Max(1,model.Instructions.Count-5));
            SetStatus(message);
        }
        public void SetPose(Vector3 point)
        {
            if(Coordinates)Coordinates.text=string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "X {0:0.0}  Y {1:0.0}  Z {2:0.0} mm",point.x*1000,point.z*1000,point.y*1000);
        }
        void OnDestroy(){if(model!=null)model.Changed-=Refresh;}
        static RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var result=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            result.SetParent(parent,false);result.anchorMin=result.anchorMax=result.pivot=new Vector2(0,1);
            result.anchoredPosition=position*Resolution;result.sizeDelta=size*Resolution;return result;
        }
        static Text Label(Transform parent,string name,Font font,int size,Color color,Vector2 position,Vector2 dimensions)
        {
            var result=Rect(parent,name,position,dimensions).gameObject.AddComponent<Text>();
            result.font=font;result.fontSize=(int)(size*Resolution);result.color=color;result.raycastTarget=false;
            result.horizontalOverflow=HorizontalWrapMode.Overflow;result.verticalOverflow=VerticalWrapMode.Truncate;
            return result;
        }
    }
}
