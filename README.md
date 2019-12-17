# SuperFastBlit
This demo demostrates the ability to display an ARGB buffer to the screen within 1ms.

SetDIBitsToDevice() can blit a 1080p buffer onto the form at speed of ~1.2ms. The source buffer must be 32 bits per pixel, otherwise the opertaion will take ~11ms.

## Here's some simple demonstration code:
```c#
//Get DC from Form HWND
TargetDC = GetDC(TargetForm.Handle);

//Create bitmap info
BITMAPINFO BINFO = new BITMAPINFO();
BINFO.bmiHeader.biBitCount = 32; //Bits Per Pixel
BINFO.bmiHeader.biWidth = targetWidth; //Width
BINFO.bmiHeader.biHeight = targetHeight; //Height, negate if image is flipped
BINFO.bmiHeader.biPlanes = 1; //Planes, leave at 1
unsafe {
  BINFO.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
}

//Blit the argb buffer to the screen
SetDIBitsToDevice(TargetDC, 0, 0, (uint)targetWidth, (uint)targetHeight, 0, 0, 0, (uint)targetHeight, ptr, ref BINFO, 0);

//Release the DC when done
ReleaseDC(TargetForm.Handle, TargetDC)
```

![Exmaple of near 1080p Blit](https://i.imgur.com/dd8Eh4B.png)
