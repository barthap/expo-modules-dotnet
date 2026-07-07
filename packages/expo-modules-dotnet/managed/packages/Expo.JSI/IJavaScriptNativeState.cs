namespace Expo.JSI;

public interface IJavaScriptNativeState<TSelf>
    where TSelf : class, IJavaScriptNativeState<TSelf>
{
  static abstract JavaScriptNativeStateTypeId TypeId { get; }
}
