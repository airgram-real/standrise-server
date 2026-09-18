using Google.Protobuf;
using Google.Protobuf.Reflection;
using pb = Google.Protobuf;

namespace Axlebolt.Bolt.Protobuf
{
	public sealed class GetRecipeStatusRequest : pb::IMessage<GetRecipeStatusRequest>
	{
		private static readonly MessageParser<GetRecipeStatusRequest> _parser =
			new MessageParser<GetRecipeStatusRequest>(() => new GetRecipeStatusRequest());
		public static MessageParser<GetRecipeStatusRequest> Parser => _parser;
		public static MessageDescriptor Descriptor => null;
		MessageDescriptor pb::IMessage.Descriptor => null;

		private string recipeCode_ = "";
		public string RecipeCode
		{
			get => recipeCode_;
			set => recipeCode_ = value ?? "";
		}

		public GetRecipeStatusRequest() { }
		public GetRecipeStatusRequest(GetRecipeStatusRequest other) : this() { recipeCode_ = other.recipeCode_; }
		public GetRecipeStatusRequest Clone() => new GetRecipeStatusRequest(this);
		public override bool Equals(object other) => Equals(other as GetRecipeStatusRequest);
		public bool Equals(GetRecipeStatusRequest other) => other != null && RecipeCode == other.RecipeCode;
		public override int GetHashCode() => RecipeCode?.GetHashCode() ?? 0;
		public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

		public void WriteTo(CodedOutputStream output)
		{
			if (!string.IsNullOrEmpty(RecipeCode))
			{
				output.WriteRawTag(10);
				output.WriteString(RecipeCode);
			}
		}

		public int CalculateSize() =>
			string.IsNullOrEmpty(RecipeCode) ? 0 : 1 + CodedOutputStream.ComputeStringSize(RecipeCode);

		public void MergeFrom(GetRecipeStatusRequest other)
		{
			if (other == null) return;
			if (!string.IsNullOrEmpty(other.RecipeCode)) RecipeCode = other.RecipeCode;
		}

		public void MergeFrom(CodedInputStream input)
		{
			uint tag;
			while ((tag = input.ReadTag()) != 0)
			{
				if (tag == 10) RecipeCode = input.ReadString();
				else input.SkipLastField();
			}
		}
	}

	public sealed class GetRecipeStatusResponse : pb::IMessage<GetRecipeStatusResponse>
	{
		private static readonly MessageParser<GetRecipeStatusResponse> _parser =
			new MessageParser<GetRecipeStatusResponse>(() => new GetRecipeStatusResponse());
		public static MessageParser<GetRecipeStatusResponse> Parser => _parser;
		public static MessageDescriptor Descriptor => null;
		MessageDescriptor pb::IMessage.Descriptor => null;

		private bool executionIntervalOk_;
		private bool executionTimingOk_;
		private int timesExecutedTotal_;

		public bool ExecutionIntervalOk
		{
			get => executionIntervalOk_;
			set => executionIntervalOk_ = value;
		}

		public bool ExecutionTimingOk
		{
			get => executionTimingOk_;
			set => executionTimingOk_ = value;
		}

		public int TimesExecutedTotal
		{
			get => timesExecutedTotal_;
			set => timesExecutedTotal_ = value;
		}

		public GetRecipeStatusResponse() { }
		public GetRecipeStatusResponse(GetRecipeStatusResponse other) : this()
		{
			executionIntervalOk_ = other.executionIntervalOk_;
			executionTimingOk_ = other.executionTimingOk_;
			timesExecutedTotal_ = other.timesExecutedTotal_;
		}
		public GetRecipeStatusResponse Clone() => new GetRecipeStatusResponse(this);
		public override bool Equals(object other) => Equals(other as GetRecipeStatusResponse);
		public bool Equals(GetRecipeStatusResponse other) =>
			other != null
			&& ExecutionIntervalOk == other.ExecutionIntervalOk
			&& ExecutionTimingOk == other.ExecutionTimingOk
			&& TimesExecutedTotal == other.TimesExecutedTotal;
		public override int GetHashCode() =>
			((ExecutionIntervalOk ? 1 : 0) * 397) ^ ((ExecutionTimingOk ? 1 : 0) * 397) ^ TimesExecutedTotal;
		public override string ToString() => pb::JsonFormatter.ToDiagnosticString(this);

		public void WriteTo(CodedOutputStream output)
		{
			if (ExecutionIntervalOk)
			{
				output.WriteRawTag(8);
				output.WriteBool(ExecutionIntervalOk);
			}
			if (ExecutionTimingOk)
			{
				output.WriteRawTag(16);
				output.WriteBool(ExecutionTimingOk);
			}
			if (TimesExecutedTotal != 0)
			{
				output.WriteRawTag(24);
				output.WriteInt32(TimesExecutedTotal);
			}
		}

		public int CalculateSize()
		{
			int size = 0;
			if (ExecutionIntervalOk) size += 2;
			if (ExecutionTimingOk) size += 2;
			if (TimesExecutedTotal != 0) size += 1 + CodedOutputStream.ComputeInt32Size(TimesExecutedTotal);
			return size;
		}

		public void MergeFrom(GetRecipeStatusResponse other)
		{
			if (other == null) return;
			if (other.ExecutionIntervalOk) ExecutionIntervalOk = true;
			if (other.ExecutionTimingOk) ExecutionTimingOk = true;
			if (other.TimesExecutedTotal != 0) TimesExecutedTotal = other.TimesExecutedTotal;
		}

		public void MergeFrom(CodedInputStream input)
		{
			uint tag;
			while ((tag = input.ReadTag()) != 0)
			{
				switch (tag)
				{
					case 8: ExecutionIntervalOk = input.ReadBool(); break;
					case 16: ExecutionTimingOk = input.ReadBool(); break;
					case 24: TimesExecutedTotal = input.ReadInt32(); break;
					default: input.SkipLastField(); break;
				}
			}
		}
	}
}
