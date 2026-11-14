using System.ComponentModel.DataAnnotations;
using Volo.Abp.Identity;
using Volo.Abp.ObjectExtending;
using Volo.Abp.Threading;

namespace SwapMid;

public static class SwapMidModuleExtensionConfigurator
{
    private static readonly OneTimeRunner OneTimeRunner = new OneTimeRunner();

    public static void Configure()
    {
        OneTimeRunner.Run(() =>
        {
            ConfigureExistingProperties();
            ConfigureExtraProperties();
        });
    }

    private static void ConfigureExistingProperties()
    {
        /* You can change max lengths for properties of the
         * entities defined in the modules used by your application.
         *
         * Example: Change user and role name max lengths

           AbpUserConsts.MaxNameLength = 99;
           IdentityRoleConsts.MaxNameLength = 99;

         * Notice: It is not suggested to change property lengths
         * unless you really need it. Go with the standard values wherever possible.
         *
         * If you are using EF Core, you will need to run the add-migration command after your changes.
         */
    }

    private static void ConfigureExtraProperties()
    {
        // 用户头像：在 IdentityUser 上加扩展属性 AvatarBlobName（存 BlobStoring 里的头像 blob 名，形如 "{userId:N}{ext}"）。
        // 不 MapEfCoreProperty → 默认落 AbpUsers.ExtraProperties 的 JSON 列，**零迁移**。头像字节走 BlobStoring
        // （AvatarContainer，DB provider），此处只存引用。读写见 AvatarManager / AvatarController。
        ObjectExtensionManager.Instance.Modules()
            .ConfigureIdentity(identity =>
            {
                identity.ConfigureUser(user =>
                {
                    user.AddOrUpdateProperty<string>(
                        SwapMid.BlobStoring.AvatarConsts.BlobNamePropertyName,
                        property =>
                        {
                            property.Attributes.Add(new StringLengthAttribute(128));
                        });
                });

                // 角色描述：IdentityRole 上加扩展属性 Description，落 AbpRoles.ExtraProperties 的 JSONB 列，**零迁移**。
                // IdentityRoleDto/IdentityRoleCreateDto/IdentityRoleUpdateDto 均继承 ExtendableObject，
                // 注册后 ABP 内置的 Entity<->Dto ExtraProperties 映射会自动透传该键（AccountRoleAppService 里
                // 显式收窄成 AccountRoleDto.Description 字段，不裸透传整个 ExtraProperties，见该域 CLAUDE.md）。
                identity.ConfigureRole(role =>
                {
                    role.AddOrUpdateProperty<string>(
                        "Description",
                        property =>
                        {
                            property.Attributes.Add(new StringLengthAttribute(512));
                        });
                });
            });
    }
}
