namespace Maple2.Model.Validators;

// RequestTaxi 0x0048. Modes 0 and 5 have no body: the client sends 0 from the taxi station's action
// key and 5 from its station finder when the station's field is not in its list of known stations.
// Modes 1, 3 and 4 name the destination field.
public static class TaxiRules {
    public const byte DiscoverAtStation = 0;
    public const byte DiscoverByFinder = 5;

    public static bool IsDiscover(byte mode) {
        return mode is DiscoverAtStation or DiscoverByFinder;
    }

    // A station is found once, and only in a field that has one.
    public static bool CanDiscover(bool known, bool fieldHasStation) {
        return !known && fieldHasStation;
    }

    // Client CashCallManager (cashcallmanager.cpp FUN_140b030b0) picks how an air taxi (Rotors) ride is paid. Premium
    // club rides free to any field; a field the character has visited (its Unlock.Maps, the list ServerEnter sends) takes
    // one walkie-talkie or the meso fare (mode 3); a field never visited takes 3 advanced walkie-talkies or the meret
    // price (mode 4). The client never sends mode 2.
    public const int UnvisitedAdvancedTickets = 3;

    public static bool CanMesoRide(bool visited, bool premium) {
        return visited || premium;
    }

    // The walkie-talkies one ride takes, 0 when the item cannot reach the field (s_err_cash_taxi_need_advanced_ticket:
    // a plain walkie-talkie only reaches a visited field).
    public static int TicketCost(bool advanced, bool visited) {
        if (visited) {
            return 1;
        }
        return advanced ? UnvisitedAdvancedTickets : 0;
    }

    // UIWorldMapDialog shows the meso fare and the meret price scaled by 1 - the character's air taxi fee reduce rate
    // (special attribute 0xaa) through ScaleValue (truncate after adding 0.000001), never below 0.
    public static long AirFare(long price, float reduceRate) {
        long cost = (long) ((double) price * (double) (1.0f - reduceRate) + 0.000001);
        return cost < 0 ? 0 : cost;
    }
}
