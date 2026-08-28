using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UProjectHub.App.Behaviors;

public static class MotionFeedbackBehavior
{
    public static readonly DependencyProperty DurationProperty = DependencyProperty.RegisterAttached(
        "Duration",
        typeof(Duration),
        typeof(MotionFeedbackBehavior),
        new PropertyMetadata(new Duration(TimeSpan.Zero), OnDurationChanged));

    public static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.RegisterAttached(
        "EasingFunction",
        typeof(IEasingFunction),
        typeof(MotionFeedbackBehavior));

    public static readonly DependencyProperty PressedScaleProperty = DependencyProperty.RegisterAttached(
        "PressedScale",
        typeof(double),
        typeof(MotionFeedbackBehavior),
        new PropertyMetadata(0.98));

    public static readonly DependencyProperty IsPressFeedbackEnabledProperty = DependencyProperty.RegisterAttached(
        "IsPressFeedbackEnabled",
        typeof(bool),
        typeof(MotionFeedbackBehavior),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsPressFeedbackActiveProperty = DependencyProperty.RegisterAttached(
        "IsPressFeedbackActive",
        typeof(bool),
        typeof(MotionFeedbackBehavior),
        new PropertyMetadata(false, OnIsPressFeedbackActiveChanged));

    public static readonly DependencyProperty RestingOpacityProperty = DependencyProperty.RegisterAttached(
        "RestingOpacity",
        typeof(double),
        typeof(MotionFeedbackBehavior),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty ActiveOpacityProperty = DependencyProperty.RegisterAttached(
        "ActiveOpacity",
        typeof(double),
        typeof(MotionFeedbackBehavior),
        new PropertyMetadata(1d));

    public static readonly DependencyProperty IsOpacityFeedbackEnabledProperty = DependencyProperty.RegisterAttached(
        "IsOpacityFeedbackEnabled",
        typeof(bool),
        typeof(MotionFeedbackBehavior),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsOpacityFeedbackActiveProperty = DependencyProperty.RegisterAttached(
        "IsOpacityFeedbackActive",
        typeof(bool),
        typeof(MotionFeedbackBehavior),
        new PropertyMetadata(false, OnIsOpacityFeedbackActiveChanged));

    public static Duration GetDuration(DependencyObject element) =>
        (Duration)element.GetValue(DurationProperty);

    public static void SetDuration(DependencyObject element, Duration value) =>
        element.SetValue(DurationProperty, value);

    public static IEasingFunction? GetEasingFunction(DependencyObject element) =>
        (IEasingFunction?)element.GetValue(EasingFunctionProperty);

    public static void SetEasingFunction(DependencyObject element, IEasingFunction? value) =>
        element.SetValue(EasingFunctionProperty, value);

    public static double GetPressedScale(DependencyObject element) =>
        (double)element.GetValue(PressedScaleProperty);

    public static void SetPressedScale(DependencyObject element, double value) =>
        element.SetValue(PressedScaleProperty, value);

    public static bool GetIsPressFeedbackEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsPressFeedbackEnabledProperty);

    public static void SetIsPressFeedbackEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsPressFeedbackEnabledProperty, value);

    public static bool GetIsPressFeedbackActive(DependencyObject element) =>
        (bool)element.GetValue(IsPressFeedbackActiveProperty);

    public static void SetIsPressFeedbackActive(DependencyObject element, bool value) =>
        element.SetValue(IsPressFeedbackActiveProperty, value);

    public static double GetRestingOpacity(DependencyObject element) =>
        (double)element.GetValue(RestingOpacityProperty);

    public static void SetRestingOpacity(DependencyObject element, double value) =>
        element.SetValue(RestingOpacityProperty, value);

    public static double GetActiveOpacity(DependencyObject element) =>
        (double)element.GetValue(ActiveOpacityProperty);

    public static void SetActiveOpacity(DependencyObject element, double value) =>
        element.SetValue(ActiveOpacityProperty, value);

    public static bool GetIsOpacityFeedbackEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsOpacityFeedbackEnabledProperty);

    public static void SetIsOpacityFeedbackEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsOpacityFeedbackEnabledProperty, value);

    public static bool GetIsOpacityFeedbackActive(DependencyObject element) =>
        (bool)element.GetValue(IsOpacityFeedbackActiveProperty);

    public static void SetIsOpacityFeedbackActive(DependencyObject element, bool value) =>
        element.SetValue(IsOpacityFeedbackActiveProperty, value);

    private static void OnIsPressFeedbackActiveChanged(
        DependencyObject element,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return;
        }

        ApplyPressFeedback(frameworkElement);
    }

    private static void OnIsOpacityFeedbackActiveChanged(
        DependencyObject element,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (element is UIElement uiElement)
        {
            ApplyOpacityFeedback(uiElement);
        }
    }

    private static void OnDurationChanged(
        DependencyObject element,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var duration = (Duration)eventArgs.NewValue;
        if (!duration.HasTimeSpan || duration.TimeSpan != TimeSpan.Zero)
        {
            return;
        }

        if (GetIsPressFeedbackEnabled(element) && element is FrameworkElement frameworkElement)
        {
            ApplyPressFeedback(frameworkElement);
        }

        if (GetIsOpacityFeedbackEnabled(element) && element is UIElement uiElement)
        {
            ApplyOpacityFeedback(uiElement);
        }
    }

    private static void ApplyPressFeedback(FrameworkElement frameworkElement)
    {
        var scale = frameworkElement.RenderTransform is ScaleTransform existingScale
            ? existingScale.CloneCurrentValue()
            : new ScaleTransform(1, 1);
        frameworkElement.RenderTransform = scale;

        var targetScale = GetIsPressFeedbackActive(frameworkElement)
            ? GetPressedScale(frameworkElement)
            : 1d;
        Animate(scale, ScaleTransform.ScaleXProperty, targetScale, frameworkElement);
        Animate(scale, ScaleTransform.ScaleYProperty, targetScale, frameworkElement);
    }

    private static void ApplyOpacityFeedback(UIElement uiElement)
    {
        var targetOpacity = GetIsOpacityFeedbackActive(uiElement)
            ? GetActiveOpacity(uiElement)
            : GetRestingOpacity(uiElement);
        Animate(uiElement, UIElement.OpacityProperty, targetOpacity, uiElement);
    }

    private static void Animate(
        Animatable target,
        DependencyProperty property,
        double to,
        DependencyObject resourceOwner)
    {
        var duration = GetDuration(resourceOwner);
        if (duration.HasTimeSpan && duration.TimeSpan == TimeSpan.Zero)
        {
            target.BeginAnimation(property, null);
            target.SetCurrentValue(property, to);
            return;
        }

        target.BeginAnimation(
            property,
            new DoubleAnimation
            {
                To = to,
                Duration = duration,
                EasingFunction = GetEasingFunction(resourceOwner),
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void Animate(
        UIElement target,
        DependencyProperty property,
        double to,
        DependencyObject resourceOwner)
    {
        var duration = GetDuration(resourceOwner);
        if (duration.HasTimeSpan && duration.TimeSpan == TimeSpan.Zero)
        {
            target.BeginAnimation(property, null);
            target.SetCurrentValue(property, to);
            return;
        }

        target.BeginAnimation(
            property,
            new DoubleAnimation
            {
                To = to,
                Duration = duration,
                EasingFunction = GetEasingFunction(resourceOwner),
            },
            HandoffBehavior.SnapshotAndReplace);
    }
}
