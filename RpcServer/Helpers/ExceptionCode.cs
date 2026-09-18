using System;

namespace StandRiseServer.RpcServer.Helpers
{
    public class ClanNotFoundRpcException : Exception { }
    public class PlayerIsAlreadyInClanRpcException : Exception { }
    public class InvalidClanNameRpcException : Exception { }
    public class ClanNameAlreadyExistsRpcException : Exception { }
    public class InvalidClanTagRpcException : Exception { }
    public class ClanTagAlreadyExistsRpcException : Exception { }
    public class WrongCurrencyRpcException : Exception { }
    public class CouponHasAlreadyActivatedRpcException : Exception { }
    public class ActiveCouponNotFoundRpcException : Exception { }
    public class DeviceInfoNotFoundException : Exception { }
    public class AccountNotFoundException : Exception { }
    public class HashDocumentNotFoundException : Exception { }
    public class ClanHasAlreadyFilledRpcException : Exception { }
    public class ClanInvitedPlayerAlreadyInClanRpcException : Exception { }
    public class ClanInvitedPlayerInCooldownRpcException : Exception { }
    public class ClanInvitedPlayerIsAlreadyInvitedRpcException : Exception { }
    public class ClanPlayerInCooldownRpcException : Exception { }
    public class ClanPlayerWasBannedRpcException : Exception { }
    public class ClanWrongTypeRpcException : Exception { }

}
