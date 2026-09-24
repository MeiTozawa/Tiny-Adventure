using System;

namespace TinyAdventure
{
    /// <summary>
    /// 値を返さない操作の回復可能エラーを表す軽量な値型です。
    /// 失敗が許容されない操作はこれを使わず void と Assert を使用してください。
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

        public TResult Match<TResult>(Func<TResult> onOk, Func<GameError, TResult> onErr)
        {
            if (onOk == null) throw new ArgumentNullException(nameof(onOk));
            if (onErr == null) throw new ArgumentNullException(nameof(onErr));
            return IsOk ? onOk() : onErr(Error);
        }

        public void Match(Action onOk, Action<GameError> onErr)
        {
            if (onOk == null) throw new ArgumentNullException(nameof(onOk));
            if (onErr == null) throw new ArgumentNullException(nameof(onErr));
            if (IsOk)
            {
                onOk();
            }
            else
            {
                onErr(Error);
            }
        }

        public Result Bind(Func<Result> binder)
        {
            if (binder == null) throw new ArgumentNullException(nameof(binder));
            return IsOk ? binder() : this;
        }

        public Result<TNext> Bind<TNext>(Func<Result<TNext>> binder)
        {
            if (binder == null) throw new ArgumentNullException(nameof(binder));
            return IsOk ? binder() : Result<TNext>.Err(Error);
        }

        public Result Tap(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (IsOk) action();
            return this;
        }

        public Result TapErr(Action<GameError> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (IsErr) action(Error);
            return this;
        }

        public Result OrElse(Action<GameError> onErr)
        {
            if (onErr == null) throw new ArgumentNullException(nameof(onErr));
            if (IsErr) onErr(Error);
            return this;
        }

        public Result LogIfErr(UnityEngine.Object context = null, string prefix = "")
        {
            if (IsErr)
            {
                string message = string.IsNullOrEmpty(prefix)
                    ? $"[Result 拒絶] {Error}"
                    : $"{prefix}: {Error}";
                if (context != null)
                {
                    UnityEngine.Debug.LogWarning(message, context);
                }
                else
                {
                    UnityEngine.Debug.LogWarning(message);
                }
            }
            return this;
        }

        public bool Equals(Result other) => IsOk == other.IsOk && Error == other.Error;
        public override bool Equals(object obj) => obj is Result other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(IsOk, (int)Error);
        public static bool operator ==(Result left, Result right) => left.Equals(right);
        public static bool operator !=(Result left, Result right) => !left.Equals(right);

        public override string ToString() => IsOk ? "Result.Ok" : $"Result.Err({Error})";
    }

    /// <summary>
    /// 値を返す操作の成功結果または回復可能エラーを表すジェネリック値型です。
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

        public static implicit operator Result<T>(T value) => Ok(value);
        public static implicit operator Result<T>(GameError error) => Err(error);
        public static implicit operator Result(Result<T> result) => result.IsOk ? Result.Ok() : Result.Err(result.Error);

        public TResult Match<TResult>(Func<T, TResult> onOk, Func<GameError, TResult> onErr)
        {
            if (onOk == null) throw new ArgumentNullException(nameof(onOk));
            if (onErr == null) throw new ArgumentNullException(nameof(onErr));
            return IsOk ? onOk(Value) : onErr(Error);
        }

        public void Match(Action<T> onOk, Action<GameError> onErr)
        {
            if (onOk == null) throw new ArgumentNullException(nameof(onOk));
            if (onErr == null) throw new ArgumentNullException(nameof(onErr));
            if (IsOk)
            {
                onOk(Value);
            }
            else
            {
                onErr(Error);
            }
        }

        public Result<TNext> Map<TNext>(Func<T, TNext> mapper)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            return IsOk ? Result<TNext>.Ok(mapper(Value)) : Result<TNext>.Err(Error);
        }

        public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> binder)
        {
            if (binder == null) throw new ArgumentNullException(nameof(binder));
            return IsOk ? binder(Value) : Result<TNext>.Err(Error);
        }

        public Result Bind(Func<T, Result> binder)
        {
            if (binder == null) throw new ArgumentNullException(nameof(binder));
            return IsOk ? binder(Value) : Result.Err(Error);
        }

        public Result<T> Tap(Action<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (IsOk) action(Value);
            return this;
        }

        public Result<T> TapErr(Action<GameError> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (IsErr) action(Error);
            return this;
        }

        public T OrElse(Func<GameError, T> fallback)
        {
            if (fallback == null) throw new ArgumentNullException(nameof(fallback));
            return IsOk ? Value : fallback(Error);
        }

        public Result<T> LogIfErr(UnityEngine.Object context = null, string prefix = "")
        {
            if (IsErr)
            {
                string message = string.IsNullOrEmpty(prefix)
                    ? $"[Result<{typeof(T).Name}> 拒絶] {Error}"
                    : $"{prefix}: {Error}";
                if (context != null)
                {
                    UnityEngine.Debug.LogWarning(message, context);
                }
                else
                {
                    UnityEngine.Debug.LogWarning(message);
                }
            }
            return this;
        }

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

            return System.Collections.Generic.EqualityComparer<T>.Default.Equals(Value, other.Value);
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
