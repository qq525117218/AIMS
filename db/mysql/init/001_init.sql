CREATE TABLE `auth_info` (
                             `app_id` char(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '应用ID',
                             `app_secret` char(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '应用密钥',
                             `app_name` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '应用名称',
                             `description` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '应用描述',
                             `active_flag` bit(1) DEFAULT b'1' COMMENT '是否激活',
                             `created_time` datetime DEFAULT NULL COMMENT '创建时间',
                             `updated_time` datetime DEFAULT NULL COMMENT '更新时间',
                             PRIMARY KEY (`app_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci  COMMENT "认证表";

CREATE TABLE `base_jobs` (
                             `id` bigint unsigned NOT NULL AUTO_INCREMENT COMMENT "主键ID",
                             `queue_name` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '队列名',
                             `task_type` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '内部类型，例如 copywriter，ai_sku等',
                             `job_id` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '任务唯一标识',
                             `app_id` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '应用ID',
                             `status` tinyint unsigned NOT NULL DEFAULT '0' COMMENT '说明查看 enums/base_jobs_status.py',
                             `tag` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '业务侧自定义，回调回传',
                             `result` json DEFAULT NULL COMMENT '任务结果',
                             `callback` json DEFAULT NULL COMMENT '回调 {url :  class:  function:  }',
                             `exception` text CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci COMMENT '异常',
                             `payload` json DEFAULT NULL COMMENT '参数',
                             `option` json DEFAULT NULL COMMENT '可选',
                             `created_at` datetime NOT NULL COMMENT '创建时间',
                             `updated_at` datetime NOT NULL COMMENT '更新时间',
                             PRIMARY KEY (`id`),
                             KEY `idx_type` (`task_type`) USING BTREE,
                             KEY `uq_job_id` (`job_id`) USING BTREE
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='基础公共队列';

CREATE TABLE `files_oss` (
                             `file_id` bigint unsigned NOT NULL AUTO_INCREMENT COMMENT '主键ID',
                             `file_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '文件名称',
                             `user_id` char(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '用户ID',
                             `file_path` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '路径',
                             `file_type` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL COMMENT '文件类型，image video 等',
                             `created_at` timestamp NULL DEFAULT NULL COMMENT '创建时间',
                             `updated_at` timestamp NULL DEFAULT NULL COMMENT '更新时间',
                             `deleted_at` timestamp NULL DEFAULT NULL COMMENT '删除时间',
                             PRIMARY KEY (`file_id`)
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='文件表';