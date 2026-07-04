namespace SluiceBase.Core.Servers;

// How to reach a MongoDB deployment. Standard = a single host:port (or the default
// scheme). Srv = a mongodb+srv DNS seedlist name (Atlas / managed clusters); the port
// is not used in this mode.
public enum ConnectionMode
{
    Standard,
    Srv
}
