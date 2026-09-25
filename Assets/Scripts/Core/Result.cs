using System;
using System.Collections.Generic;

namespace TinyAdventure
{
    /// <summary>
    /// 値を返さない操作の成否と領域エラーを表す軽量な零割当値型です。
    /// 失敗が許容されない契約違反はこれを使わず Assert を使用してください。
    /// </summary>
    public readonly struct Result : IEquatable<Result>
    {
        public bool IsOk { get; }
        public bool IsErr => !IsOk;
        public GameError Error { get; }

        private Result(bool isOk, GameError error)
        {
            IsOk = isOk;
            Error = error;
        }

        public static Result Ok() => new(true, GameError.None);
        public static Result Err(GameError error) => new(false, error);

        public static implicit operator Result(GameError error) => Err(error);

        public bool Equals(Result other) => IsOk == other.IsOk && Error == other.Error;
        public override bool Equals(object obj) => obj is Result other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(IsOk, (int)Error);
        public static bool operator ==(Result left, Result right) => left.Equals(right);
        public static bool operator !=(Result left, Result right) => !left.Equals(right);

        public override string ToString() => IsOk ? "Result.Ok" : $"Result.Err({Error})";
    }

    /// <summary>
    /// 値を返す操作の成功結果または領域エラーを表すジェネリック零割当値型です。
    /// </summary>
    public readonly struct Result<T> : IEquatable<Result<T>>
    {
        public bool IsOk { get; }
        public bool IsErr => !IsOk;
        public T Value { get; }
        public GameError Error { get; }

        private Result(T value)
        {
            IsOk = true;
            Value = value;
            Error = GameError.None;
        }

        private Result(GameError error)
        {
            IsOk = false;
            Value = default;
            Error = error;
        }

        public static Result<T> Ok(T value) => new(value);
        public static Result<T> Err(GameError error) => new(error);

        public T UnwrapOr(T defaultValue) => IsOk ? Value : defaultValue;

        public bool TryGet(out T value)
        {
            value = Value;
            return IsOk;
        }

        public static implicit operator Result<T>(T value) => Ok(value);
        public static implicit operator Result<T>(GameError error) => Err(error);
        public static implicit operator Result(Result<T> result) => result.IsOk ? Result.Ok() : Result.Err(result.Error);

        public bool Equals(Result<T> other)
        {
            if (IsOk != other.IsOk || Error != other.Error)
            {
                return false;
            }

            if (!IsOk)
            {
                return true;
            }

            return EqualityComparer<T>.Default.Equals(Value, other.Value);
        }

        public override bool Equals(object obj) => obj is Result<T> other && Equals(other);

        public override int GetHashCode()
        {
            return IsOk
                ? HashCode.Combine(true, Value != null ? Value.GetHashCode() : 0)
                : HashCode.Combine(false, (int)Error);
        }

        public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);
        public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

        public override string ToString() => IsOk ? $"Result<{typeof(T).Name}>.Ok({Value})" : $"Result<{typeof(T).Name}>.Err({Error})";
    }
}
