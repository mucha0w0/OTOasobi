using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Otoasobi;

public sealed partial class SpanEditor
{
    void ArrangeOtoWave()
    {
        var count = Math.Min(_lo.Length, _hi.Length);
        var visible = _otoWave != null && count > 0 && _file != null && OtoHeight > 1;
        if (_otoWave == null || _otoWaveLine == null)
            return;
        if (!visible)
        {
            _otoWave.Points = new PointCollection();
            _otoWaveLine.Points = new PointCollection();
            if (_otoZero != null)
                _otoZero.Visibility = Visibility.Collapsed;
            return;
        }

        if (_otoZero != null)
        {
            var y = OtoTop + OtoHeight * 0.5;
            _otoZero.Visibility = Visibility.Visible;
            _otoZero.X1 = Pad;
            _otoZero.X2 = Math.Max(Pad, _plot.Width - Pad);
            _otoZero.Y1 = y;
            _otoZero.Y2 = y;
        }

        if (_samples)
        {
            _otoWave.Points = new PointCollection();
            _otoWaveLine.Points = SamplePoints(OtoY);
            _otoWaveLine.Visibility = Visibility.Visible;
            return;
        }

        _otoWaveLine.Points = new PointCollection();
        _otoWaveLine.Visibility = Visibility.Collapsed;
        _otoWave.Points = WavePoints(OtoY);
    }

    void AddOtoVisuals()
    {
        if (_file == null)
            return;

        _otoWave = new Polygon
        {
            Fill = WaveBrush,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_otoWave);
        _otoWaveLine = new Polyline
        {
            Stroke = WaveLineBrush,
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_otoWaveLine);
        _otoZero = new Line
        {
            Stroke = ZeroBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        _plot.Children.Add(_otoZero);

        OtoMarks.Fill(_file, _oto, _otoMarks, _loudness);
        foreach (var _ in _otoMarks)
            AddOtoMarkVisual();

        LiftOtoChrome();
    }

    void EnsureOtoVisuals(int count)
    {
        if (count <= _otoRects.Count)
            return;
        while (_otoRects.Count < count)
            AddOtoMarkVisual();
        LiftOtoChrome();
    }

    void AddOtoMarkVisual()
    {
        var rect = new Rectangle
        {
            Fill = OtoRegionBrush,
            IsHitTestVisible = false,
        };
        var text = new TextBlock
        {
            Foreground = TextBrush,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        _otoRects.Add(rect);
        _otoTexts.Add(text);
        _plot.Children.Add(rect);
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            var line = new Line
            {
                Stroke = OtoBrush(point),
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            var name = new TextBlock
            {
                Text = OtoPointName(point),
                Foreground = OtoBrush(point),
                FontSize = 10,
                IsHitTestVisible = false,
            };
            var chip = new Border
            {
                Background = OtoChipBrush,
                Padding = new Thickness(3, 0, 3, 0),
                IsHitTestVisible = false,
                Child = name,
            };
            _otoLines.Add(line);
            _otoPointChips.Add(chip);
            _plot.Children.Add(line);
        }

        _plot.Children.Add(text);
    }

    void LiftOtoChrome()
    {
        foreach (var chip in _otoPointChips)
        {
            _plot.Children.Remove(chip);
            _plot.Children.Add(chip);
        }
    }

    void ArrangeOtoLabels()
    {
        if (_file == null)
            return;
        UseBand(_otoView);
        RefreshOtoMarks();
        EnsureOtoVisuals(_otoMarks.Count);
        var count = Math.Min(_otoMarks.Count, _otoRects.Count);
        if (_focusOto >= count)
            _focusOto = -1;
        var lanes = LaneOf(_otoMarks, count);
        var laneCount = 1;
        for (var i = 0; i < count; i++)
            laneCount = Math.Max(laneCount, lanes[i] + 1);

        var usable = Math.Max(0, OtoHeight - 8);
        var pitch = laneCount <= 1 ? 16 : Math.Min(16, usable / laneCount);
        var top = OtoTop;
        var height = OtoHeight;
        for (var i = 0; i < count; i++)
        {
            var mark = _otoMarks[i];
            var start = Math.Min(mark.Offset, mark.Blank);
            var end = Math.Max(mark.Offset, mark.Blank);
            var left = X(start);
            var width = Math.Max(0, X(end) - left);
            var focused = _focusOto >= 0;
            var chosen = i == _focusOto;
            var dim = focused && !chosen;
            var rect = _otoRects[i];
            rect.Fill = chosen || (!focused && i == _hotOto) ? OtoRegionHotBrush : OtoRegionBrush;
            rect.Opacity = dim ? OtoDimOpacity : 1;
            rect.Visibility = width < 1 || height < 2 ? Visibility.Collapsed : Visibility.Visible;
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            rect.Width = width;
            rect.Height = Math.Max(0, height);

            var textTop = top + 4 + lanes[i] * pitch;
            var text = _otoTexts[i];
            text.Text = AliasLabel(mark, i);
            text.Width = width;
            text.Opacity = dim ? OtoDimOpacity : 1;
            text.FontWeight = chosen
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            var aliasWide = chosen ? width >= 12 : width >= 28;
            var showAlias = aliasWide && height >= 20 && textTop + 14 <= top + height - 2;
            text.Visibility = showAlias ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(text, left);
            Canvas.SetTop(text, textTop);

            for (var point = 0; point < OtoTune.PointCount; point++)
            {
                var line = _otoLines[i * OtoTune.PointCount + point];
                var x = X(mark.At(point));
                var dragging = chosen && _drag == DragKind.Oto && _otoDragIndex == i && _otoStack[point];
                line.Opacity = dim ? OtoDimOpacity : 1;
                line.StrokeThickness = dragging ? 4 : chosen ? 3 : 2;
                line.Visibility = height > 1 ? Visibility.Visible : Visibility.Collapsed;
                line.X1 = x;
                line.X2 = x;
                line.Y1 = chosen ? 0 : top;
                line.Y2 = top + height;
            }
        }

        for (var i = count; i < _otoRects.Count; i++)
            _otoRects[i].Visibility = Visibility.Collapsed;
        for (var i = count; i < _otoTexts.Count; i++)
            _otoTexts[i].Visibility = Visibility.Collapsed;
        for (var i = count * OtoTune.PointCount; i < _otoLines.Count; i++)
            _otoLines[i].Visibility = Visibility.Collapsed;

        var textBottom = top + 4 + laneCount * Math.Max(pitch, 14);
        var nameY = top + height - 16;
        var showNames = count > 0 && height >= 56 && nameY >= textBottom;
        if (!showNames)
        {
            foreach (var chip in _otoPointChips)
                chip.Visibility = Visibility.Collapsed;
        }
        else
        {
            PlaceOtoNames(count, nameY);
        }

        if (_focusOto >= 0 && _drag != DragKind.Oto)
            RaiseFocusedOto();
        PlaceWaveOtoLines();
    }

    void PlaceWaveOtoLines()
    {
        PlaceOverlayOtoLines(_waveView, _waveOtoLines);
        PlaceOverlayOtoLines(_specView, _specOtoLines);
        PlaceOverlayOtoLines(_loudView, _loudOtoLines);
    }

    void PlaceOverlayOtoLines(BandView band, Line[] lines)
    {
        var height = ContentHeight(band);
        var show = _focusOto >= 0 && _focusOto < _otoMarks.Count && height > 1;
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            var line = lines[point];
            if (!show)
            {
                line.Visibility = Visibility.Collapsed;
                continue;
            }

            var x = XOn(band, _otoMarks[_focusOto].At(point));
            var dragging = _drag == DragKind.Oto && _otoDragIndex == _focusOto && _otoStack[point];
            line.Visibility = Visibility.Visible;
            line.X1 = x;
            line.X2 = x;
            line.Y1 = 0;
            line.Y2 = height;
            line.StrokeThickness = dragging ? 4 : 3;
        }
    }

    static Line MakeOverlayOtoLine(int point) => new()
    {
        Stroke = OtoBrush(point),
        StrokeThickness = 3,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    void RaiseFocusedOto()
    {
        if (_focusOto < 0 || _focusOto >= _otoRects.Count)
            return;
        Raise(_otoRects[_focusOto]);
        for (var point = 0; point < OtoTune.PointCount; point++)
            Raise(_otoLines[_focusOto * OtoTune.PointCount + point]);
        if (_focusOto < _otoTexts.Count)
            Raise(_otoTexts[_focusOto]);
        foreach (var chip in _otoPointChips)
            Raise(chip);
        var start = _focusOto * OtoTune.PointCount;
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            if (start + point < _otoPointChips.Count)
                Raise(_otoPointChips[start + point]);
        }
    }

    void Raise(UIElement element)
    {
        var children = _plot.Children;
        var index = children.IndexOf(element);
        if (index < 0)
            return;
        var last = children.Count - 1;
        if (index >= last)
            return;
        children.Move((uint)index, (uint)last);
    }

    void PlaceOtoNames(int count, double nameY)
    {
        var pending = new List<(double X, int Index)>();
        for (var i = 0; i < count; i++)
        {
            for (var point = 0; point < OtoTune.PointCount; point++)
            {
                var index = i * OtoTune.PointCount + point;
                var x = X(_otoMarks[i].At(point));
                if (x < -30 || x > _plot.Width + 30)
                {
                    _otoPointChips[index].Visibility = Visibility.Collapsed;
                    continue;
                }

                pending.Add((x, index));
            }
        }

        var focused = _focusOto >= 0 && _focusOto < count;
        pending.Sort((a, b) =>
        {
            if (focused)
            {
                var rank = (a.Index / OtoTune.PointCount == _focusOto ? 0 : 1)
                    .CompareTo(b.Index / OtoTune.PointCount == _focusOto ? 0 : 1);
                if (rank != 0)
                    return rank;
            }

            var star = (Tuned(b.Index / OtoTune.PointCount, b.Index % OtoTune.PointCount) ? 1 : 0)
                .CompareTo(Tuned(a.Index / OtoTune.PointCount, a.Index % OtoTune.PointCount) ? 1 : 0);
            if (star != 0)
                return star;
            return a.X.CompareTo(b.X);
        });
        var occupied = new List<(double Left, double Right)>();
        var shown = new bool[_otoPointChips.Count];
        foreach (var item in pending)
        {
            var chip = _otoPointChips[item.Index];
            if (chip.Child is not TextBlock text)
            {
                chip.Visibility = Visibility.Collapsed;
                continue;
            }

            var markIndex = item.Index / OtoTune.PointCount;
            var point = item.Index % OtoTune.PointCount;
            text.Text = PointLabel(markIndex, point);
            var width = Math.Max(12, text.Text.Length * 10 + 8);
            var left = item.X - width / 2;
            var right = left + width;
            var blocked = false;
            foreach (var box in occupied)
            {
                if (left < box.Right && right > box.Left)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                chip.Visibility = Visibility.Collapsed;
                continue;
            }

            var dim = focused && item.Index / OtoTune.PointCount != _focusOto;
            chip.Opacity = dim ? OtoDimOpacity : 1;
            chip.Visibility = Visibility.Visible;
            Canvas.SetLeft(chip, left);
            Canvas.SetTop(chip, nameY);
            shown[item.Index] = true;
            occupied.Add((left, right + 4));
        }

        for (var i = 0; i < _otoPointChips.Count; i++)
        {
            if (!shown[i])
                _otoPointChips[i].Visibility = Visibility.Collapsed;
        }
    }

    void ShowOtoHover(double x)
    {
        if (_otoMarks.Count == 0)
        {
            SetHotOto(-1);
            return;
        }

        var best = -1;
        var bestDist = HitPx;
        for (var i = 0; i < _otoMarks.Count; i++)
        {
            var mark = _otoMarks[i];
            for (var point = 0; point < OtoTune.PointCount; point++)
            {
                var dist = Math.Abs(X(mark.At(point)) - x);
                if (dist > bestDist)
                    continue;
                bestDist = dist;
                best = i;
            }
        }

        if (best >= 0)
        {
            SetHotOto(best);
            return;
        }

        SetHotOto(HitOtoRegion(XToTime(x)));
    }

    bool ReleaseFocusAt(double y, double x)
    {
        if (_focusOto < 0 || _hitKind == BandKind.Oto)
            return false;
        return !FocusedOwnsTime(XToTime(x));
    }

    // 強調中の oto が占める音。母音の行は、別の行になっていない直前の子音も含む。
    bool FocusedOwnsTime(double time)
    {
        if (_file == null || _focusOto < 0)
            return false;
        var spans = _file.Spans;
        for (var i = 0; i < spans.Count; i++)
        {
            if (time < spans[i].Start - 1e-4 || time > spans[i].End + 1e-4)
                continue;
            if (FocusedOwnsSpan(i))
                return true;
        }

        return false;
    }

    bool FocusedOwnsSpan(int index)
    {
        var anchor = AnchorSpan(_focusOto);
        if (anchor < 0 || _file == null || index < 0)
            return false;
        if (index == anchor)
            return true;
        var spans = _file.Spans;
        if (index != anchor - 1 || anchor >= spans.Count || SpanAnchorsOtherOto(index))
            return false;
        if (!IsVowelLabel(spans[anchor].Label))
            return false;
        var consonant = spans[index];
        var vowel = spans[anchor];
        return consonant.Token == vowel.Token
            && !string.IsNullOrEmpty(consonant.Token)
            && !IsVowelLabel(consonant.Label)
            && !IsQuietSpan(consonant.Label);
    }

    bool SpanAnchorsOtherOto(int span)
    {
        for (var i = 0; i < _otoMarks.Count; i++)
        {
            if (i != _focusOto && AnchorSpan(i) == span)
                return true;
        }

        return false;
    }

    int AnchorSpan(int otoIndex)
    {
        if (_file == null || otoIndex < 0)
            return -1;
        var source = otoIndex < _otoAuto.Count ? _otoAuto : _otoMarks;
        if (otoIndex >= source.Count)
            return -1;
        var time = source[otoIndex].Preutter;
        var spans = _file.Spans;
        var best = -1;
        var bestDelta = double.MaxValue;
        for (var i = 0; i < spans.Count; i++)
        {
            if (time < spans[i].Start - 0.002 || time > spans[i].End + 0.002)
                continue;
            var delta = Math.Abs(spans[i].Start - time);
            if (delta >= bestDelta)
                continue;
            bestDelta = delta;
            best = i;
        }

        return best;
    }

    void FocusOtoAt(double x)
    {
        var hits = OtoHitsAt(x);
        int next;
        if (hits.Count == 0)
            next = -1;
        else if (_focusOto >= 0 && hits.Contains(_focusOto))
            next = hits.Count == 1
                ? _focusOto
                : hits[(hits.IndexOf(_focusOto) + 1) % hits.Count];
        else
            next = BestOtoHit(hits, x);

        SetFocusOto(next);
        if (_focusOto < 0)
            ShowOtoHover(x);
    }

    void SetFocusOto(int index)
    {
        if (index >= _otoMarks.Count)
            index = -1;
        if (_focusOto == index)
            return;
        _focusOto = index;
        _hotOto = -1;
        if (_file != null && HasDrawnPlot)
            ArrangeOtoLabels();
        PaintWaveBoundaryOpacity();
    }

    List<int> OtoHitsAt(double x)
    {
        var time = XToTime(x);
        var hits = new List<int>();
        for (var i = 0; i < _otoMarks.Count; i++)
        {
            if (OtoContains(_otoMarks[i], time) || OtoPointNear(_otoMarks[i], x))
                hits.Add(i);
        }

        hits.Sort((a, b) =>
        {
            var cmp = _otoMarks[a].Offset.CompareTo(_otoMarks[b].Offset);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });
        return hits;
    }

    int BestOtoHit(List<int> hits, double x)
    {
        var best = hits[0];
        var bestDist = HitPx;
        var foundLine = false;
        foreach (var i in hits)
        {
            var mark = _otoMarks[i];
            for (var point = 0; point < OtoTune.PointCount; point++)
            {
                var dist = Math.Abs(X(mark.At(point)) - x);
                if (dist > bestDist)
                    continue;
                bestDist = dist;
                best = i;
                foundLine = true;
            }
        }

        if (foundLine)
            return best;

        var bestWidth = double.MaxValue;
        foreach (var i in hits)
        {
            var width = Math.Abs(_otoMarks[i].Blank - _otoMarks[i].Offset);
            if (width >= bestWidth)
                continue;
            bestWidth = width;
            best = i;
        }

        return best;
    }

    static bool OtoContains(OtoMark mark, double time)
    {
        var start = Math.Min(mark.Offset, mark.Blank);
        var end = Math.Max(mark.Offset, mark.Blank);
        return time >= start - 1e-9 && time <= end + 1e-9;
    }

    bool OtoPointNear(OtoMark mark, double x)
    {
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            if (Math.Abs(X(mark.At(point)) - x) <= HitPx)
                return true;
        }

        return false;
    }

    int HitOtoRegion(double time)
    {
        var best = -1;
        var bestWidth = double.MaxValue;
        for (var i = 0; i < _otoMarks.Count; i++)
        {
            var start = Math.Min(_otoMarks[i].Offset, _otoMarks[i].Blank);
            var end = Math.Max(_otoMarks[i].Offset, _otoMarks[i].Blank);
            if (time < start - 1e-9 || time > end + 1e-9)
                continue;
            var width = end - start;
            if (width < bestWidth)
            {
                bestWidth = width;
                best = i;
            }
        }

        return best;
    }

    void SetHotOto(int index)
    {
        if (_focusOto >= 0)
            return;
        if (index >= _otoMarks.Count)
            index = -1;
        if (_hotOto == index)
            return;
        PaintOtoRegion(_hotOto, hot: false);
        _hotOto = index;
        PaintOtoRegion(index, hot: true);
    }

    void PaintOtoRegion(int index, bool hot)
    {
        if (index < 0 || index >= _otoRects.Count)
            return;
        _otoRects[index].Fill = hot ? OtoRegionHotBrush : OtoRegionBrush;
    }

    bool InOtoBand(double y) => _hitKind == BandKind.Oto && _file != null && y >= 0 && y < _otoView.Plot.Height;

    static int[] LaneOf(List<OtoMark> marks, int count)
    {
        var lanes = new int[count];
        var ends = new List<double>();
        for (var i = 0; i < count; i++)
        {
            var start = Math.Min(marks[i].Offset, marks[i].Blank);
            var end = Math.Max(marks[i].Offset, marks[i].Blank);
            var lane = ends.FindIndex(edge => start >= edge - 1e-4);
            if (lane < 0)
            {
                lane = ends.Count;
                ends.Add(end);
            }
            else
            {
                ends[lane] = end;
            }

            lanes[i] = lane;
        }

        return lanes;
    }

    void RefreshOtoMarks()
    {
        if (_file == null)
            return;
        OtoMarks.Fill(_file, _oto, _otoAuto, _loudness);
        TakePending(_otoAuto.Count);
        DropStaleTunes(_otoAuto.Count);
        _otoMarks.Clear();
        var duration = Duration;
        for (var i = 0; i < _otoAuto.Count; i++)
        {
            _tune.TryGetValue(i, out var points);
            _otoMarks.Add(OtoTune.Apply(_otoAuto[i], points, duration));
        }
    }

    void TakePending(int count)
    {
        if (_pendingTunes == null || _file == null)
            return;
        foreach (var entry in _pendingTunes)
        {
            if (entry.Index < 0 || entry.Index >= count)
                continue;
            if (!entry.File.Equals(_file.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(entry.Alias, _otoAuto[entry.Index].Alias, StringComparison.Ordinal))
                continue;
            var points = entry.Points();
            if (!OtoTune.Has(points))
                continue;
            _tune[entry.Index] = points;
        }

        _pendingTunes = null;
    }

    void DropStaleTunes(int count)
    {
        var drop = new List<int>();
        foreach (var index in _tune.Keys)
        {
            if (index < 0 || index >= count)
                drop.Add(index);
        }

        foreach (var index in drop)
            _tune.Remove(index);
    }

    void ArmOtoStack(int index, int point)
    {
        Array.Clear(_otoStack);
        Array.Clear(_otoDragOrigin);
        if (index < 0 || index >= _otoMarks.Count)
            return;
        var mark = _otoMarks[index];
        var at = mark.At(point);
        for (var i = 0; i < OtoTune.PointCount; i++)
        {
            _otoStack[i] = Math.Abs(mark.At(i) - at) <= OtoTune.MagnetSeconds;
            _otoDragOrigin[i] = TunedAt(index, i);
        }

        _otoStack[point] = true;
    }

    void ApplyOtoDrag(double time)
    {
        var index = _otoDragIndex;
        if (_file == null || index < 0 || index >= _otoMarks.Count || index >= _otoAuto.Count)
            return;
        var shown = _otoMarks[index];
        var auto = _otoAuto[index];
        var next = OtoTune.ClampGroup(shown, _otoStack, time, Duration);
        var moved = false;
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            if (!_otoStack[point])
                continue;
            double? stored = Math.Abs(next - auto.At(point)) <= OtoTune.MagnetSeconds ? null : next;
            if (Nullable.Equals(stored, TunedAt(index, point)))
                continue;
            SetTuned(index, point, stored);
            moved = true;
        }

        if (!moved)
            return;
        ReapplyTunedMark(index);
        if (StickTunedToShown(index))
            ReapplyTunedMark(index);
        PaintDraggedOto(index);
        _changed = !SameTune(index, _otoDragOrigin);
    }

    bool StickTunedToShown(int index)
    {
        if (index < 0 || index >= _otoMarks.Count || index >= _otoAuto.Count)
            return false;
        var changed = false;
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            if (!_otoStack[point] || TunedAt(index, point) == null)
                continue;
            var shown = _otoMarks[index].At(point);
            if (Math.Abs(shown - _otoAuto[index].At(point)) <= OtoTune.MagnetSeconds)
            {
                SetTuned(index, point, null);
                changed = true;
                continue;
            }

            if (Math.Abs(shown - TunedAt(index, point)!.Value) <= 1e-9)
                continue;
            SetTuned(index, point, OtoTune.RoundSeconds(shown));
            changed = true;
        }

        return changed;
    }

    void ReapplyTunedMark(int index)
    {
        if (index < 0 || index >= _otoAuto.Count || index >= _otoMarks.Count)
            return;
        _tune.TryGetValue(index, out var points);
        _otoMarks[index] = OtoTune.Apply(_otoAuto[index], points, Duration);
    }

    // ドラッグ中は、動かしている音素の線だけを動かす。全音素の再計算と重ね順の入れ替えはしない。
    void PaintDraggedOto(int index)
    {
        if (index < 0 || index >= _otoMarks.Count || index >= _otoRects.Count)
            return;
        var mark = _otoMarks[index];
        var start = Math.Min(mark.Offset, mark.Blank);
        var end = Math.Max(mark.Offset, mark.Blank);
        var left = XOn(_otoView, start);
        var width = Math.Max(0, XOn(_otoView, end) - left);
        var rect = _otoRects[index];
        rect.Width = width;
        Canvas.SetLeft(rect, left);
        if (index < _otoTexts.Count)
        {
            var text = _otoTexts[index];
            var alias = AliasLabel(mark, index);
            if (!string.Equals(text.Text, alias, StringComparison.Ordinal))
                text.Text = alias;
            text.Width = width;
            Canvas.SetLeft(text, left);
        }

        var chosen = index == _focusOto;
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            var lineIndex = index * OtoTune.PointCount + point;
            if (lineIndex >= _otoLines.Count)
                break;
            var line = _otoLines[lineIndex];
            var x = XOn(_otoView, mark.At(point));
            line.X1 = x;
            line.X2 = x;
            if (_otoStack[point])
                line.StrokeThickness = 4;
            else if (chosen)
                line.StrokeThickness = 3;
            if (lineIndex >= _otoPointChips.Count)
                continue;
            var chip = _otoPointChips[lineIndex];
            if (chip.Visibility != Visibility.Visible || chip.Child is not TextBlock name)
                continue;
            var label = PointLabel(index, point);
            if (!string.Equals(name.Text, label, StringComparison.Ordinal))
                name.Text = label;
            var chipWidth = Math.Max(12, name.Text.Length * 10 + 8);
            Canvas.SetLeft(chip, x - chipWidth / 2);
        }

        PlaceWaveOtoLines();
    }

    void SetTuned(int index, int point, double? time)
    {
        if (!_tune.TryGetValue(index, out var points))
        {
            if (time == null)
                return;
            points = new double?[OtoTune.PointCount];
            _tune[index] = points;
        }

        points[point] = time;
        if (!OtoTune.Has(points))
            _tune.Remove(index);
    }

    double? TunedAt(int index, int point)
    {
        if (!_tune.TryGetValue(index, out var points) || point < 0 || point >= points.Length)
            return null;
        return points[point];
    }

    bool Tuned(int index, int point) => TunedAt(index, point) != null;

    bool TunedAny(int index)
    {
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            if (Tuned(index, point))
                return true;
        }

        return false;
    }

    bool SameTune(int index, double?[] origin)
    {
        for (var point = 0; point < OtoTune.PointCount; point++)
        {
            if (!Nullable.Equals(TunedAt(index, point), origin[point]))
                return false;
        }

        return true;
    }

    void FlushDeferredTunes()
    {
        if (_tuneDefer == null)
            return;
        var pending = _tuneDefer;
        _tuneDefer = null;
        foreach (var index in pending.Distinct())
            EmitTune(index);
    }

    void EmitTune(int index)
    {
        if (_file == null || index < 0 || index >= _otoAuto.Count)
            return;
        var entry = new OtoAdjustedEntry
        {
            File = _file.Name,
            Index = index,
            Alias = _otoAuto[index].Alias,
        };
        for (var point = 0; point < OtoTune.PointCount; point++)
            entry.SetPoint(point, TunedAt(index, point));
        OtoTuned?.Invoke(this, entry);
    }

    (int Index, int Point)? HitOtoPoint(double x, double y)
    {
        if (_file == null || _labelsLocked || _otoMarks.Count == 0)
            return null;
        var inBand = InOtoBand(y);
        var onOverlay = _hitKind is BandKind.Wave or BandKind.Spec or BandKind.Loud;
        if (!inBand && !onOverlay)
            return null;
        if (!inBand && _focusOto < 0)
            return null;
        var slop = inBand || InOverlayBand(y) ? HitPx : 8;
        var best = -1;
        var bestPoint = -1;
        var bestDist = slop;
        var focused = false;
        for (var i = 0; i < _otoMarks.Count; i++)
        {
            if (!inBand && i != _focusOto)
                continue;
            var mark = _otoMarks[i];
            for (var point = 0; point < OtoTune.PointCount; point++)
            {
                var dist = Math.Abs(X(mark.At(point)) - x);
                if (dist > slop)
                    continue;
                AcceptOtoHit(ref best, ref bestPoint, ref bestDist, ref focused, i, point, dist, i == _focusOto);
            }
        }

        if (best < 0)
            return null;
        return (best, bestPoint);
    }

    void AcceptOtoHit(ref int best, ref int bestPoint, ref double bestDist, ref bool focused, int index, int point, double dist, bool isFocus)
    {
        var closer = best < 0 || dist < bestDist - 0.1;
        var preferFocus = !closer && isFocus && !focused && dist <= bestDist + 0.1;
        if (!closer && !preferFocus)
            return;
        bestDist = dist;
        best = index;
        bestPoint = point;
        focused = isFocus;
    }

    string PointLabel(int index, int point)
    {
        var name = OtoPointName(point);
        return Tuned(index, point) ? name + " *" : name;
    }

    string AliasLabel(OtoMark mark, int index) =>
        TunedAny(index) ? mark.Alias + " *" : mark.Alias;

    static string OtoPointName(int point) => point switch
    {
        0 => "オフセット",
        1 => "オーバーラップ",
        2 => "先行発声",
        3 => "子音部",
        _ => "ブランク",
    };

    static SolidColorBrush OtoBrush(int point) =>
        OtoPointBrushes[Math.Clamp(point, 0, OtoPointBrushes.Length - 1)];
}
