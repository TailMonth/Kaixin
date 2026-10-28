namespace KaixinPet.Behavior;

public sealed class YarnBallSimulator
{
    public const double RadiusDip = 11;
    private const double Gravity = 1600;
    private const double GroundRestitution = 0.45;
    private const double WallRestitution = 0.6;
    private const double RestSpeed = 8;
    private const double RestSecondsToStop = 2;

    private double _restSeconds;

    public bool Active { get; private set; }

    public double X { get; private set; }

    public double Y { get; private set; }

    public double VelocityX { get; private set; }

    public double VelocityY { get; private set; }

    public int FrameIndex { get; private set; }

    public void Start(Random random, double catCenterX, double groundY, double windowWidth)
    {
        X = Math.Clamp(catCenterX + 80 + random.NextDouble() * 60, 30, windowWidth - 30);
        Y = groundY - 50;
        VelocityX = random.NextDouble() * 160 - 80;
        VelocityY = -300 - random.NextDouble() * 160;
        FrameIndex = 0;
        _restSeconds = 0;
        Active = true;
    }

    public void Stop()
    {
        Active = false;
        _restSeconds = 0;
    }

    public bool Step(double dt, double groundY, double windowWidth)
    {
        if (!Active)
        {
            return false;
        }

        dt = Math.Clamp(dt, 0, 0.1);
        if (dt <= 0)
        {
            return true;
        }

        VelocityY += Gravity * dt;
        VelocityX *= Math.Exp(-0.45 * dt);

        X += VelocityX * dt;
        Y += VelocityY * dt;

        var floor = groundY - RadiusDip;
        if (Y >= floor)
        {
            Y = floor;
            if (VelocityY > 0)
            {
                VelocityY = -VelocityY * GroundRestitution;
                VelocityX *= 0.96;
                if (Math.Abs(VelocityY) < 35)
                {
                    VelocityY = 0;
                }
            }
        }

        if (X < RadiusDip)
        {
            X = RadiusDip;
            VelocityX = Math.Abs(VelocityX) * WallRestitution;
        }
        else if (X > windowWidth - RadiusDip)
        {
            X = windowWidth - RadiusDip;
            VelocityX = -Math.Abs(VelocityX) * WallRestitution;
        }

        if (Y < RadiusDip)
        {
            Y = RadiusDip;
            VelocityY = Math.Abs(VelocityY) * WallRestitution;
        }

        var speed = Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
        if (speed > 0.5)
        {
            FrameIndex = (FrameIndex + Math.Max(1, (int)(speed * dt / 8))) % 8;
        }

        if (speed < RestSpeed)
        {
            _restSeconds += dt;
            if (_restSeconds >= RestSecondsToStop)
            {
                Active = false;
                return false;
            }
        }
        else
        {
            _restSeconds = 0;
        }

        return true;
    }

    public void Bat(int direction, Random random)
    {
        if (!Active)
        {
            return;
        }

        VelocityX = direction * (200 + random.NextDouble() * 80);
        VelocityY = -(260 + random.NextDouble() * 160);
        _restSeconds = 0;
    }
}
