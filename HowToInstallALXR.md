# Setup guidelines

1. Ensure your account is an Oculus Developer account

2. Enable developer mode on Quest Pro device

3. Download and install apk at Quest Pro device using adb or sidequst [alxr-client-quest.apk  
    ](https://github.com/korejan/ALXR-experimental/releases/latest/download/alxr-client-quest.apk)[https://github.com/korejan/ALXR-experimental/releases](https://github.com/korejan/ALXR-experimental/releases)
    >Probably tested in version [2023.06.23](https://github.com/korejan/ALXR-experimental/releases/tag/v0.10.0%2Bnightly.2023.06.23)

4. Open the Oculus application on the PC and navigate to Settings then General. Turn on Unknown Sources. Ensure the current OpenXR Runtime is set to Oculus

5. Navigate to the Settings then Beta. Turn on Developer Runtime Features, then turn on Eye tracking over Oculus Link, and Natural Facial Expression over Oculus Link.
	  > Please leave/turn off “Pass-through over Oculus Link”
    > ![image](https://github.com/sjsanjsrh/QuestPro4Resonite/assets/16241081/fa3d61e4-30c9-4fef-8744-26f14a368a79)
    > ![image](https://github.com/sjsanjsrh/QuestPro4Resonite/assets/16241081/a428d42a-1be7-45b0-9e43-61782d63738a)

 4. Download and unzip [alxr-client-win-x64.zip  
    ](https://github.com/korejan/ALXR-experimental/releases/latest/download/alxr-client-win-x64.zip)[https://github.com/korejan/ALXR-experimental/releases](https://github.com/korejan/ALXR-experimental/releases)
    >Probably tested in version [2023.06.23](https://github.com/korejan/ALXR-experimental/releases/tag/v0.10.0%2Bnightly.2023.06.23)
 
 5. Right click on the alxr-client and create a shortcut. Feel free to move the shortcut to wherever is most convenient

 6.  Right click on the alxr-client shortcut and click properties. Under Target: append the flags ```--no-alvr-server --no-bindings``` to the end. Ensure there is a space between ```alxr-client.exe``` and the appended flags. Click ok
     > If ALXR exits with error messages about Vulkan please try using a different graphics backend, you can add either ```-gD3D12 OR -gD3D11```
