#include <math.h>
#import <Foundation/Foundation.h>
#import <Security/Security.h>
#import <TargetConditionals.h>
#if TARGET_OS_OSX
#import <Cocoa/Cocoa.h>
#else
#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#endif
#include <stdlib.h>
#include <string.h>

static NSString *TownImportedPath = nil;
static NSString *TownService(void) { return [[[NSBundle mainBundle] bundleIdentifier] stringByAppendingString:@".town.auth"]; }
static NSMutableDictionary *TownQuery(NSString *key) {
    return [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService:TownService(),(__bridge id)kSecAttrAccount:key} mutableCopy];
}
#if !TARGET_OS_OSX
static UIViewController *TownPresenter(void) {
    for (UIScene *scene in UIApplication.sharedApplication.connectedScenes) {
        if (![scene isKindOfClass:UIWindowScene.class] || scene.activationState != UISceneActivationStateForegroundActive) continue;
        for (UIWindow *window in ((UIWindowScene *)scene).windows) if (window.isKeyWindow) {
            UIViewController *vc=window.rootViewController;
            while(vc.presentedViewController)vc=vc.presentedViewController;
            return vc;
        }
    }
    return nil;
}
static CHHapticEngine *TownEngine = nil;
@interface TownImportDelegate : NSObject <UIDocumentPickerDelegate>
@end
@implementation TownImportDelegate
- (void)documentPicker:(UIDocumentPickerViewController *)controller didPickDocumentsAtURLs:(NSArray<NSURL *> *)urls {
    NSURL *url=urls.firstObject;if(!url)return;
    BOOL scoped=[url startAccessingSecurityScopedResource];
    NSNumber *size=nil;[url getResourceValue:&size forKey:NSURLFileSizeKey error:nil];
    if(size && size.unsignedLongLongValue<=24ull*1024*1024) {
        NSString *dest=[NSTemporaryDirectory() stringByAppendingPathComponent:[NSUUID.UUID.UUIDString stringByAppendingString:@".town"]];
        if([[NSFileManager defaultManager] copyItemAtPath:url.path toPath:dest error:nil])TownImportedPath=dest;
    }
    if(scoped)[url stopAccessingSecurityScopedResource];
}
@end
static TownImportDelegate *TownPickerDelegate = nil;
#endif
extern "C" {
    int TownThermalState(void) { return (int)NSProcessInfo.processInfo.thermalState; }
    int TownLowPowerMode(void) { return NSProcessInfo.processInfo.lowPowerModeEnabled ? 1 : 0; }
    void TownFree(void *ptr) { free(ptr); }
    int TownKeychainSet(const char *key, const char *value) {
        if(!key||!value)return 0;
        NSMutableDictionary *query=TownQuery([NSString stringWithUTF8String:key]);
        NSData *bytes=[[NSString stringWithUTF8String:value] dataUsingEncoding:NSUTF8StringEncoding];
        NSDictionary *attributes=@{(__bridge id)kSecValueData:bytes};
        OSStatus status=SecItemUpdate((__bridge CFDictionaryRef)query,(__bridge CFDictionaryRef)attributes);
        if(status==errSecItemNotFound) {
            [query addEntriesFromDictionary:attributes];
            query[(__bridge id)kSecAttrAccessible]=(__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
            status=SecItemAdd((__bridge CFDictionaryRef)query,NULL);
        }
        return status==errSecSuccess;
    }
    char *TownKeychainGet(const char *key) {
        if(!key)return NULL;NSMutableDictionary *query=TownQuery([NSString stringWithUTF8String:key]);
        query[(__bridge id)kSecReturnData]=@YES;query[(__bridge id)kSecMatchLimit]=(__bridge id)kSecMatchLimitOne;
        CFTypeRef result=NULL;OSStatus status=SecItemCopyMatching((__bridge CFDictionaryRef)query,&result);
        if(status!=errSecSuccess||!result)return NULL;
        NSData *bytes=CFBridgingRelease(result);NSString *value=[[NSString alloc] initWithData:bytes encoding:NSUTF8StringEncoding];
        return value ? strdup(value.UTF8String) : NULL;
    }
    void TownKeychainDelete(const char *key) { if(key)SecItemDelete((__bridge CFDictionaryRef)TownQuery([NSString stringWithUTF8String:key])); }
    void TownHaptic(int kind,float strength) {
        strength=fmaxf(0,fminf(1,strength));if(strength<=0)return;
#if TARGET_OS_OSX
        NSHapticFeedbackPattern p=kind==2?NSHapticFeedbackPatternLevelChange:NSHapticFeedbackPatternAlignment;
        [NSHapticFeedbackManager.defaultPerformer performFeedbackPattern:p performanceTime:NSHapticFeedbackPerformanceTimeNow];
#else
        if(![CHHapticEngine capabilitiesForHardware].supportsHaptics)return;
        if(!TownEngine) {
            NSError *error=nil;TownEngine=[[CHHapticEngine alloc] initAndReturnError:&error];
            if(error||!TownEngine)return;
            TownEngine.playsHapticsOnly=YES;
            TownEngine.resetHandler=^{dispatch_async(dispatch_get_main_queue(),^{[TownEngine startAndReturnError:nil];});};
        }
        NSError *error=nil;if(![TownEngine startAndReturnError:&error])return;
        NSMutableArray *events=[NSMutableArray array];
        int pulses=kind==2?3:(kind==1?2:1);
        for(int i=0;i<pulses;i++) {
            NSArray *params=@[[[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:strength],
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:kind==1?0.8f:0.35f]];
            [events addObject:[[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient parameters:params relativeTime:i*0.09]];
        }
        CHHapticPattern *pattern=[[CHHapticPattern alloc] initWithEvents:events parameters:@[] error:&error];
        id<CHHapticPatternPlayer> player=[TownEngine createPlayerWithPattern:pattern error:&error];
        [player startAtTime:CHHapticTimeImmediate error:&error];
#endif
    }
    void TownStopHaptics(void) {
#if !TARGET_OS_OSX
        [TownEngine stopWithCompletionHandler:nil];
#endif
    }
    void TownShareFile(const char *path) {
        if(!path)return;NSURL *url=[NSURL fileURLWithPath:[NSString stringWithUTF8String:path]];
#if TARGET_OS_OSX
        NSView *view=NSApplication.sharedApplication.keyWindow.contentView;if(!view)return;
        NSSharingServicePicker *picker=[[NSSharingServicePicker alloc] initWithItems:@[url]];
        [picker showRelativeToRect:view.bounds ofView:view preferredEdge:NSRectEdgeMinY];
#else
        UIViewController *vc=TownPresenter();if(!vc)return;
        UIActivityViewController *share=[[UIActivityViewController alloc] initWithActivityItems:@[url] applicationActivities:nil];
        share.popoverPresentationController.sourceView=vc.view;share.popoverPresentationController.sourceRect=CGRectMake(vc.view.bounds.size.width/2,vc.view.bounds.size.height/2,1,1);
        [vc presentViewController:share animated:YES completion:nil];
#endif
    }
    void TownPickWorld(void) {
#if TARGET_OS_OSX
        NSOpenPanel *panel=[NSOpenPanel openPanel];panel.allowsMultipleSelection=NO;panel.canChooseDirectories=NO;
        [panel beginWithCompletionHandler:^(NSModalResponse result){if(result==NSModalResponseOK)TownImportedPath=panel.URL.path;}];
#else
        UIViewController *vc=TownPresenter();if(!vc)return;
        UIDocumentPickerViewController *picker=[[UIDocumentPickerViewController alloc] initForOpeningContentTypes:@[UTTypeData] asCopy:YES];
        if(!TownPickerDelegate)TownPickerDelegate=[TownImportDelegate new];picker.delegate=TownPickerDelegate;picker.allowsMultipleSelection=NO;
        [vc presentViewController:picker animated:YES completion:nil];
#endif
    }
    char *TownPollImportedPath(void) { if(!TownImportedPath)return NULL;char *result=strdup(TownImportedPath.UTF8String);TownImportedPath=nil;return result; }
}
