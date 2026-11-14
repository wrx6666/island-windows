namespace Island;
public static class KeyBinding
{
    public const int Control=0x20000,Alt=0x40000,Shift=0x10000,KeyCode=0xffff;
    public static bool Valid(int chord)
    {
        if(chord==0)return true;
        int key=chord&KeyCode;
        return key is >6 and <=254 && key is not (16 or 17 or 18 or 91 or 92 or 229 or 231) && key is not (>=160 and <=165) && (chord&~(KeyCode|Control|Alt|Shift))==0;
    }
    public static string Label(int chord)
    {
        if(chord==0)return "Не назначено";
        int key=chord&KeyCode;
        string name=key switch {33=>"Page Up",34=>"Page Down",35=>"End",36=>"Home",37=>"←",38=>"↑",39=>"→",40=>"↓",45=>"Insert",46=>"Delete",32=>"Пробел",13=>"Enter",27=>"Escape",>=112 and <=135=>"F"+(key-111),>=96 and <=105=>"Num "+(key-96),>=48 and <=90=>((char)key).ToString(),_=>"VK "+key};
        return ((chord&Control)!=0?"Ctrl+":"")+((chord&Alt)!=0?"Alt+":"")+((chord&Shift)!=0?"Shift+":"")+name;
    }
}
