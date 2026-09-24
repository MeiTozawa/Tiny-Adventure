using NUnit.Framework;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    [TestFixture]
    public sealed class ResultTests
    {
        [Test]
        public void Result_Ok_ShouldHaveCorrectState()
        {
            Result result = Result.Ok();

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.IsErr, Is.False);
            Assert.That(result.Error, Is.EqualTo(GameError.None));
        }

        [Test]
        public void Result_Err_ShouldHaveCorrectState()
        {
            Result result = Result.Err(GameError.InvalidState);

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error, Is.EqualTo(GameError.InvalidState));
        }

        [Test]
        public void Result_ImplicitConversionFromGameError_ShouldCreateErrResult()
        {
            Result result = GameError.TargetDead;

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Error, Is.EqualTo(GameError.TargetDead));
        }

        [Test]
        public void Result_Match_ShouldBranchCorrectly()
        {
            Result okResult = Result.Ok();
            Result errResult = Result.Err(GameError.OutOfRange);

            string okMsg = okResult.Match(() => "success", err => $"error:{err}");
            string errMsg = errResult.Match(() => "success", err => $"error:{err}");

            Assert.That(okMsg, Is.EqualTo("success"));
            Assert.That(errMsg, Is.EqualTo("error:OutOfRange"));
        }

        [Test]
        public void Result_Bind_ShouldChainOperations()
        {
            Result okResult = Result.Ok();
            Result errResult = Result.Err(GameError.InvalidParameter);

            Result chainedOk = okResult.Bind(() => Result.Ok());
            Result chainedErr = okResult.Bind(() => Result.Err(GameError.CombatantNotRegistered));
            Result skippedBind = errResult.Bind(() => Result.Ok());

            Assert.That(chainedOk.IsOk, Is.True);
            Assert.That(chainedErr.IsErr, Is.True);
            Assert.That(chainedErr.Error, Is.EqualTo(GameError.CombatantNotRegistered));
            Assert.That(skippedBind.IsErr, Is.True);
            Assert.That(skippedBind.Error, Is.EqualTo(GameError.InvalidParameter));
        }

        [Test]
        public void Result_TapAndTapErr_ShouldExecuteCorrectCallback()
        {
            int tapCount = 0;
            int tapErrCount = 0;

            Result.Ok().Tap(() => tapCount++).TapErr(_ => tapErrCount++);
            Assert.That(tapCount, Is.EqualTo(1));
            Assert.That(tapErrCount, Is.EqualTo(0));

            Result.Err(GameError.AttackWindowClosed).Tap(() => tapCount++).TapErr(err =>
            {
                if (err == GameError.AttackWindowClosed) tapErrCount++;
            });
            Assert.That(tapCount, Is.EqualTo(1));
            Assert.That(tapErrCount, Is.EqualTo(1));
        }

        [Test]
        public void ResultGeneric_Ok_ShouldHoldValue()
        {
            Result<int> result = Result<int>.Ok(42);

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.IsErr, Is.False);
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(result.Error, Is.EqualTo(GameError.None));
        }

        [Test]
        public void ResultGeneric_Err_ShouldHoldError()
        {
            Result<int> result = Result<int>.Err(GameError.NoGroundFound);

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Value, Is.EqualTo(0));
            Assert.That(result.Error, Is.EqualTo(GameError.NoGroundFound));
        }

        [Test]
        public void ResultGeneric_ImplicitConversions_ShouldWorkSeamlessly()
        {
            Result<string> okResult = "hello";
            Result<string> errResult = GameError.DuplicateHitInSequence;

            Assert.That(okResult.IsOk, Is.True);
            Assert.That(okResult.Value, Is.EqualTo("hello"));

            Assert.That(errResult.IsErr, Is.True);
            Assert.That(errResult.Error, Is.EqualTo(GameError.DuplicateHitInSequence));

            Result nonGenericFromOk = okResult;
            Result nonGenericFromErr = errResult;
            Assert.That(nonGenericFromOk.IsOk, Is.True);
            Assert.That(nonGenericFromErr.IsErr, Is.True);
            Assert.That(nonGenericFromErr.Error, Is.EqualTo(GameError.DuplicateHitInSequence));
        }

        [Test]
        public void ResultGeneric_UnwrapOr_ShouldFallbackWhenError()
        {
            Result<int> ok = 10;
            Result<int> err = GameError.InvalidParameter;

            Assert.That(ok.UnwrapOr(99), Is.EqualTo(10));
            Assert.That(err.UnwrapOr(99), Is.EqualTo(99));
        }

        [Test]
        public void ResultGeneric_MapAndBind_ShouldTransformCorrectly()
        {
            Result<int> ok = 5;
            Result<string> mapped = ok.Map(x => $"Value:{x * 2}");

            Assert.That(mapped.IsOk, Is.True);
            Assert.That(mapped.Value, Is.EqualTo("Value:10"));

            Result<int> err = GameError.ActionCooldownActive;
            Result<string> mappedErr = err.Map(x => $"Value:{x}");
            Assert.That(mappedErr.IsErr, Is.True);
            Assert.That(mappedErr.Error, Is.EqualTo(GameError.ActionCooldownActive));

            Result<int> bound = ok.Bind(x => Result<int>.Ok(x + 10));
            Assert.That(bound.IsOk, Is.True);
            Assert.That(bound.Value, Is.EqualTo(15));
        }

        [Test]
        public void Result_OrElse_ShouldExecuteOnlyOnErr()
        {
            int errHandled = 0;
            Result.Ok().OrElse(_ => errHandled++);
            Assert.That(errHandled, Is.EqualTo(0));

            Result.Err(GameError.ActionNotRequested).OrElse(e =>
            {
                errHandled++;
                Assert.That(e, Is.EqualTo(GameError.ActionNotRequested));
            });
            Assert.That(errHandled, Is.EqualTo(1));
        }

        [Test]
        public void ResultGeneric_OrElse_ShouldReturnFallbackOnErr()
        {
            Result<int> ok = 42;
            Result<int> err = GameError.TargetDead;

            Assert.That(ok.OrElse(_ => 0), Is.EqualTo(42));
            Assert.That(err.OrElse(e => e == GameError.TargetDead ? -1 : 0), Is.EqualTo(-1));
        }

        [Test]
        public void Result_LogIfErr_ShouldReturnSelf()
        {
            Result ok = Result.Ok();
            Result loggedOk = ok.LogIfErr();
            Assert.That(loggedOk.IsOk, Is.True);

            Result err = Result.Err(GameError.InvalidParameter);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, "[Result 拒絶] InvalidParameter");
            Result loggedErr = err.LogIfErr();
            Assert.That(loggedErr.IsErr, Is.True);
        }
    }
}
