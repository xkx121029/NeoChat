#!/usr/bin/env python3
"""把 capture_demo.ps1 抓到的 BMP 帧拼成 GIF。

做三件事：
1) 统一裁剪：125% DPI 下 PrintWindow 只画到 DIP 尺寸（1536x790），窗口矩形剩下的部分是纯黑；
   取所有帧里"非纯黑像素"的并集包围盒，统一裁掉黑边，保证每帧对齐。
2) 量化：每帧 128 色、无抖动（抖动会让 CJK 小字发毛）。
3) 写出 GIF：frame duration 由 --duration-ms 决定，无限循环。

用法:
  python tools/demo/build_demo_gif.py --frames build/demo-frames \
      --out docs/reviews/media/M1-WIN-demo.gif --duration-ms 150
"""
import argparse
import glob
import os

from PIL import Image


def content_bbox(paths):
    """所有帧非纯黑像素的并集包围盒；抓帧本身就是整窗内容，只砍边缘黑区。"""
    left = top = None
    right = bottom = 0
    for path in paths:
        grey = Image.open(path).convert('L')
        # 阈值 8：窗口标题栏和内容都远亮于纯黑，黑边严格是 0
        mask = grey.point(lambda v: 255 if v > 8 else 0)
        box = mask.getbbox()
        if box is None:
            continue
        left = box[0] if left is None else min(left, box[0])
        top = box[1] if top is None else min(top, box[1])
        right = max(right, box[2])
        bottom = max(bottom, box[3])
    if left is None:
        raise SystemExit('所有帧都是纯黑，抓帧失败')
    return left, top, right, bottom


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--frames', required=True, help='capture_demo.ps1 输出的帧目录')
    parser.add_argument('--out', required=True, help='GIF 输出路径')
    parser.add_argument('--duration-ms', type=int, default=150)
    parser.add_argument('--colors', type=int, default=128)
    parser.add_argument('--width', type=int, default=0, help='可选缩放宽度，0 表示保持原始像素')
    parser.add_argument('--prune', action='store_true', help='生成后删除 BMP 帧')
    args = parser.parse_args()

    paths = sorted(glob.glob(os.path.join(args.frames, 'frame*.bmp')))
    if len(paths) < 2:
        raise SystemExit('帧数不足：%s' % (paths,))

    box = content_bbox(paths)
    palette_frames = []
    for path in paths:
        image = Image.open(path).convert('RGB').crop(box)
        if args.width:
            height = round(image.height * args.width / image.width)
            image = image.resize((args.width, height), Image.LANCZOS)
        palette_frames.append(image.quantize(colors=args.colors, method=Image.MEDIANCUT, dither=Image.NONE))

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    palette_frames[0].save(
        args.out,
        save_all=True,
        append_images=palette_frames[1:],
        duration=args.duration_ms,
        loop=0,
        optimize=True,
        disposal=1,
    )

    size = palette_frames[0].size
    duration = len(palette_frames) * args.duration_ms / 1000
    print('gif: %s' % args.out)
    print('frames=%d size=%dx%d duration=%.2fs bytes=%d'
          % (len(palette_frames), size[0], size[1], duration, os.path.getsize(args.out)))
    print('crop=left%d top%d right%d bottom%d' % box)

    if args.prune:
        for path in paths:
            os.remove(path)
        os.rmdir(args.frames)


if __name__ == '__main__':
    main()
