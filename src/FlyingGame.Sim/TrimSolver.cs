using FlyingGame.Core;
using FlyingGame.Core.Aero;
using FlyingGame.Core.DataContracts;
using FlyingGame.Core.MathTypes;

namespace FlyingGame.Sim;

/// <summary>
/// Solves straight, wings-level trim for an unpowered glider: finds angle of attack, pitch attitude, and
/// elevator deflection such that the net specific force and pitching moment are (near) zero at a target
/// airspeed. Since there's no thrust, trim is a steady descent — theta comes out negative, and the ratio
/// of horizontal to vertical speed is the trimmed glide ratio.
/// </summary>
public static class TrimSolver
{
    public readonly struct Result
    {
        public readonly bool Converged;
        public readonly int Iterations;
        public readonly double AlphaRad;
        public readonly double ThetaRad;
        public readonly double ElevatorRad;
        public readonly double ResidualNorm;
        public readonly double GlideRatio;

        public Result(bool converged, int iterations, double alphaRad, double thetaRad, double elevatorRad, double residualNorm, double glideRatio)
        {
            Converged = converged;
            Iterations = iterations;
            AlphaRad = alphaRad;
            ThetaRad = thetaRad;
            ElevatorRad = elevatorRad;
            ResidualNorm = residualNorm;
            GlideRatio = glideRatio;
        }
    }

    public static Result SolveGliderTrim(
        AircraftConfig config,
        double iasMs,
        double altitudeM = 0.0,
        int maxIterations = 100,
        double tolerance = 1e-9)
    {
        Dictionary<string, AirfoilTable> tables = Aircraft.BuildAirfoilTables(config);
        double airDensity = Atmosphere.DensityAtAltitude(altitudeM);
        double mass = config.Mass.MassKg;
        double weight = mass * Atmosphere.GravityMs2;
        double elevatorLimit = config.Controls.Elevator.MaxDeflRad;

        double[] x = { 5.0 * Math.PI / 180.0, -3.0 * Math.PI / 180.0, 0.0 };
        double[] residual = Residual(x);
        int iteration = 0;

        for (; iteration < maxIterations; iteration++)
        {
            residual = Residual(x);
            if (Norm(residual) < tolerance)
            {
                break;
            }

            double[,] jacobian = NumericJacobian(x, residual);
            double[] delta = SolveLinear3(jacobian, residual);

            double[] maxStep = { 5.0 * Math.PI / 180.0, 5.0 * Math.PI / 180.0, elevatorLimit * 0.5 };
            for (int k = 0; k < 3; k++)
            {
                x[k] -= Math.Clamp(delta[k], -maxStep[k], maxStep[k]);
            }

            x[0] = Math.Clamp(x[0], -20.0 * Math.PI / 180.0, 25.0 * Math.PI / 180.0);
            x[1] = Math.Clamp(x[1], -45.0 * Math.PI / 180.0, 45.0 * Math.PI / 180.0);
            x[2] = Math.Clamp(x[2], -elevatorLimit, elevatorLimit);
        }

        residual = Residual(x);
        double finalNorm = Norm(residual);
        double glideRatio = ComputeGlideRatio(x[0], x[2]);

        return new Result(finalNorm < tolerance, iteration, x[0], x[1], x[2], finalNorm, glideRatio);

        double[] Residual(double[] xv)
        {
            double alpha = xv[0], theta = xv[1], elevatorRad = xv[2];
            Vec3 bodyVelocity = new(iasMs * Math.Cos(alpha), 0, iasMs * Math.Sin(alpha));
            ControlDeflections controls = new(0, elevatorRad, 0, 0);
            (Vec3 force, Vec3 moment) = AeroModel.Compute(config, tables, bodyVelocity, Vec3.Zero, Vec3.Zero, airDensity, controls);

            double gravityX = -weight * Math.Sin(theta);
            double gravityZ = weight * Math.Cos(theta);

            double fxEq = (force.X + gravityX) / weight;
            double fzEq = (force.Z + gravityZ) / weight;
            double mEq = moment.Y / weight; // normalized by weight * 1 m reference length

            return new[] { fxEq, fzEq, mEq };
        }

        double[,] NumericJacobian(double[] xv, double[] f0)
        {
            double[,] jacobian = new double[3, 3];
            const double h = 1e-6;
            for (int col = 0; col < 3; col++)
            {
                double[] perturbed = (double[])xv.Clone();
                perturbed[col] += h;
                double[] f1 = Residual(perturbed);
                for (int row = 0; row < 3; row++)
                {
                    jacobian[row, col] = (f1[row] - f0[row]) / h;
                }
            }

            return jacobian;
        }

        double ComputeGlideRatio(double alpha, double elevatorRad)
        {
            Vec3 bodyVelocity = new(iasMs * Math.Cos(alpha), 0, iasMs * Math.Sin(alpha));
            ControlDeflections controls = new(0, elevatorRad, 0, 0);
            (Vec3 force, _) = AeroModel.Compute(config, tables, bodyVelocity, Vec3.Zero, Vec3.Zero, airDensity, controls);

            double velDirX = Math.Cos(alpha), velDirZ = Math.Sin(alpha);
            double drag = -(force.X * velDirX + force.Z * velDirZ);
            double lift = force.X * velDirZ - force.Z * velDirX;
            return drag > 1e-9 ? lift / drag : double.NaN;
        }
    }

    private static double Norm(double[] v)
    {
        double sum = 0;
        foreach (double c in v)
        {
            sum += c * c;
        }

        return Math.Sqrt(sum);
    }

    /// <summary>Solves the 3x3 linear system a*x = b via Gaussian elimination with partial pivoting.</summary>
    private static double[] SolveLinear3(double[,] a, double[] b)
    {
        const int n = 3;
        double[,] m = (double[,])a.Clone();
        double[] rhs = (double[])b.Clone();

        for (int col = 0; col < n; col++)
        {
            int pivotRow = col;
            double maxAbs = Math.Abs(m[col, col]);
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(m[row, col]) > maxAbs)
                {
                    maxAbs = Math.Abs(m[row, col]);
                    pivotRow = row;
                }
            }

            if (pivotRow != col)
            {
                for (int c = 0; c < n; c++)
                {
                    (m[col, c], m[pivotRow, c]) = (m[pivotRow, c], m[col, c]);
                }

                (rhs[col], rhs[pivotRow]) = (rhs[pivotRow], rhs[col]);
            }

            double diag = Math.Abs(m[col, col]) < 1e-12 ? 1e-12 : m[col, col];
            for (int row = col + 1; row < n; row++)
            {
                double factor = m[row, col] / diag;
                for (int c = col; c < n; c++)
                {
                    m[row, c] -= factor * m[col, c];
                }

                rhs[row] -= factor * rhs[col];
            }
        }

        double[] x = new double[n];
        for (int row = n - 1; row >= 0; row--)
        {
            double sum = rhs[row];
            for (int c = row + 1; c < n; c++)
            {
                sum -= m[row, c] * x[c];
            }

            double diag = Math.Abs(m[row, row]) < 1e-12 ? 1e-12 : m[row, row];
            x[row] = sum / diag;
        }

        return x;
    }
}
