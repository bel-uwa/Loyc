﻿
// This is a generated file
using System;
using System.Collections.Generic;
using Loyc.Math;

namespace Loyc.Geometry
{
	using T = System.Int32;
	using BoundingBox = BoundingBox<int>;
	using System;

	public static partial class BoundingBoxMath
	{
		public static void Deflate(this BoundingBox self, T amountX, T amountY) { Inflate(self, -amountX, -amountY); }
		public static void Inflate(this BoundingBox self, T amountX, T amountY)
		{
			if (amountX < 0 && -amountX * 2 >= self.Width())
				self.SetXAndWidth(MathEx.Average(self.X1, self.X2), 0);
			else
				self.SetXAndWidth(self.X1 - amountX, self.X2 + amountX);
			if (amountY < 0 && -amountY * 2 <= self.Height())
				self.SetYAndHeight(MathEx.Average(self.Y1, self.Y2), 0);
			else
				self.SetYAndHeight(self.Y1 - amountY, self.Y2 + amountY);
		}
		public static BoundingBox Deflated(this BoundingBox self, T amountX, T amountY)
		{
			var copy = self.Clone();
			Inflate(copy, -amountX, -amountY);
			return copy;
		}
		public static BoundingBox Inflated(this BoundingBox self, T amountX, T amountY)
		{
			var copy = self.Clone();
			Inflate(copy, amountX, amountY);
			return copy;
		}

		public static T Width(this BoundingBox bb) { return bb.X2 - bb.X1; }
		public static T Height(this BoundingBox bb) { return bb.Y2 - bb.Y1; }

		public static Point<T> ProjectOnto(this Point<T> p, BoundingBox bbox)
		{
			return new Point<T>(MathEx.InRange(p.X, bbox.X1, bbox.X2), MathEx.InRange(p.X, bbox.X1, bbox.X2));
		}

		public static Point<T> Center(this BoundingBox<T> self)
		{
			return new Point<T>(MathEx.Average(self.X1, self.X2), MathEx.Average(self.Y1, self.Y2));
		}
	}
}
namespace Loyc.Geometry
{
	using T = System.Single;
	using BoundingBox = BoundingBox<float>;
	using System;

	public static partial class BoundingBoxMath
	{
		public static void Deflate(this BoundingBox self, T amountX, T amountY) { Inflate(self, -amountX, -amountY); }
		public static void Inflate(this BoundingBox self, T amountX, T amountY)
		{
			if (amountX < 0 && -amountX * 2 >= self.Width())
				self.SetXAndWidth(MathEx.Average(self.X1, self.X2), 0);
			else
				self.SetXAndWidth(self.X1 - amountX, self.X2 + amountX);
			if (amountY < 0 && -amountY * 2 <= self.Height())
				self.SetYAndHeight(MathEx.Average(self.Y1, self.Y2), 0);
			else
				self.SetYAndHeight(self.Y1 - amountY, self.Y2 + amountY);
		}
		public static BoundingBox Deflated(this BoundingBox self, T amountX, T amountY)
		{
			var copy = self.Clone();
			Inflate(copy, -amountX, -amountY);
			return copy;
		}
		public static BoundingBox Inflated(this BoundingBox self, T amountX, T amountY)
		{
			var copy = self.Clone();
			Inflate(copy, amountX, amountY);
			return copy;
		}

		public static T Width(this BoundingBox bb) { return bb.X2 - bb.X1; }
		public static T Height(this BoundingBox bb) { return bb.Y2 - bb.Y1; }

		public static Point<T> ProjectOnto(this Point<T> p, BoundingBox bbox)
		{
			return new Point<T>(MathEx.InRange(p.X, bbox.X1, bbox.X2), MathEx.InRange(p.X, bbox.X1, bbox.X2));
		}

		public static Point<T> Center(this BoundingBox<T> self)
		{
			return new Point<T>(MathEx.Average(self.X1, self.X2), MathEx.Average(self.Y1, self.Y2));
		}
	}
}
namespace Loyc.Geometry
{
	using T = System.Double;
	using BoundingBox = BoundingBox<double>;
	using System;

	public static partial class BoundingBoxMath
	{
		public static void Deflate(this BoundingBox self, T amountX, T amountY) { Inflate(self, -amountX, -amountY); }
		public static void Inflate(this BoundingBox self, T amountX, T amountY)
		{
			if (amountX < 0 && -amountX * 2 >= self.Width())
				self.SetXAndWidth(MathEx.Average(self.X1, self.X2), 0);
			else
				self.SetXAndWidth(self.X1 - amountX, self.X2 + amountX);
			if (amountY < 0 && -amountY * 2 <= self.Height())
				self.SetYAndHeight(MathEx.Average(self.Y1, self.Y2), 0);
			else
				self.SetYAndHeight(self.Y1 - amountY, self.Y2 + amountY);
		}
		public static BoundingBox Deflated(this BoundingBox self, T amountX, T amountY)
		{
			var copy = self.Clone();
			Inflate(copy, -amountX, -amountY);
			return copy;
		}
		public static BoundingBox Inflated(this BoundingBox self, T amountX, T amountY)
		{
			var copy = self.Clone();
			Inflate(copy, amountX, amountY);
			return copy;
		}

		public static T Width(this BoundingBox bb) { return bb.X2 - bb.X1; }
		public static T Height(this BoundingBox bb) { return bb.Y2 - bb.Y1; }

		public static Point<T> ProjectOnto(this Point<T> p, BoundingBox bbox)
		{
			return new Point<T>(MathEx.InRange(p.X, bbox.X1, bbox.X2), MathEx.InRange(p.X, bbox.X1, bbox.X2));
		}

		public static Point<T> Center(this BoundingBox<T> self)
		{
			return new Point<T>(MathEx.Average(self.X1, self.X2), MathEx.Average(self.Y1, self.Y2));
		}
	}
}

namespace Loyc.Geometry
{
	public static partial class BoundingBoxMath
	{
		public static Point<T> ProjectOnto<T>(this Point<T> p, BoundingBox<T> bbox) where T : IConvertible, IComparable<T>, IEquatable<T>
		{
			return new Point<T>(MathEx.InRange(p.X, bbox.X1, bbox.X2), MathEx.InRange(p.X, bbox.X1, bbox.X2));
		}
	}
}