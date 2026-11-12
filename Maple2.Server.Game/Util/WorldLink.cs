using Grpc.Core;

namespace Maple2.Server.Game.Util;

// A running Game server's registration with the World server. The World server keeps its channels and its
// players in memory only, so one that was restarted knows neither: the Game server asks for its own channel id
// again and reports the sessions it holds.
public class WorldLink {
    public enum Step {
        Known, // the World server knows this channel
        Down, // the World server does not answer
        Registered, // the World server did not know this channel and has taken it
        Refused, // the World server gave the id to another Game server
    }

    private readonly int channel;
    private readonly Func<(int Channel, bool Known)> register;
    private readonly Func<bool> announce;
    private bool announceDue;

    public Step Last { get; private set; } = Step.Known;

    // register: asks the World server for this channel's id; it throws RpcException while the World is down.
    // announce: reports the sessions; false when it could not finish, it is then tried again on the next check.
    public WorldLink(int channel, Func<(int Channel, bool Known)> register, Func<bool> announce) {
        this.channel = channel;
        this.register = register;
        this.announce = announce;
    }

    public Step Check() {
        Step step;
        try {
            (int given, bool known) = register();
            step = given != channel ? Step.Refused : known ? Step.Known : Step.Registered;
        } catch (RpcException) {
            step = Step.Down;
        }

        if (step == Step.Registered) {
            announceDue = true;
        }
        if (announceDue && step is Step.Registered or Step.Known) {
            try {
                announceDue = !announce();
            } catch (RpcException) {
                announceDue = true;
            }
        }

        Last = step;
        return step;
    }
}
