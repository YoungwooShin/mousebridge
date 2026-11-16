namespace MouseBridge.Core;
public sealed class ControlLease
{
    public Guid Session{get;private set;}
    private long lastSeen;
    public bool TryOpen(Guid id,byte version,bool allowed,long now)
    {
        if(!allowed||version!=InputWire.Version||id==Guid.Empty||(Session!=Guid.Empty&&Session!=id))return false;
        Session=id;lastSeen=now;return true;
    }
    public bool Touch(Guid id,long now)
    {if(Session==Guid.Empty||id!=Session)return false;lastSeen=now;return true;}
    public bool Expired(long now)=>Session!=Guid.Empty&&now-lastSeen>2000;
    public Guid Revoke(){Guid old=Session;Session=Guid.Empty;return old;}
}
