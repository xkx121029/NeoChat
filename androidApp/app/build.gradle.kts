plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.ksp)
}

android {
    namespace = "com.neochat.app"
    compileSdk = 37
    compileSdkMinor = 1

    defaultConfig {
        applicationId = "com.neochat.app"
        minSdk = 26
        targetSdk = 37
        versionCode = 1
        versionName = "0.0.0-M0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildFeatures {
        compose = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    lint {
        abortOnError = true
        warningsAsErrors = true
        checkDependencies = true
        disable += setOf("GradleDependency", "NewerVersionAvailable", "AndroidGradlePluginVersion")
    }

    testOptions {
        unitTests.isIncludeAndroidResources = true
        unitTests.all {
            it.useJUnitPlatform()
            it.systemProperty("neochat.spec.sql", rootProject.file("spec/schema/v1.sql").absolutePath)
            it.systemProperty(
                "neochat.schema.room",
                layout.projectDirectory.file("../schema/room-v1.json").asFile.absolutePath,
            )
            listOf("neochat.crash.db", "neochat.crash.phase").forEach { key ->
                System.getProperty(key)?.let { value -> it.systemProperty(key, value) }
            }
            it.maxHeapSize = "1536m"
            it.testLogging {
                events("passed", "failed", "skipped")
                exceptionFormat = org.gradle.api.tasks.testing.logging.TestExceptionFormat.FULL
            }
        }
    }
}

ksp {
    arg("room.schemaLocation", "$projectDir/schemas")
}

dependencies {
    implementation(project(":androidApp:domain"))
    implementation(project(":androidApp:protocol"))
    implementation(platform(libs.compose.bom))
    implementation(libs.compose.ui)
    implementation(libs.compose.material3)

    implementation(libs.room.runtime)
    implementation(libs.room.ktx)
    implementation(libs.sqlite.bundled)
    ksp(libs.room.compiler)

    testImplementation(libs.junit.jupiter)
    testRuntimeOnly(libs.junit.platform.launcher)
    testImplementation(libs.junit4)
    testRuntimeOnly(libs.junit.vintage.engine)
    testImplementation(libs.robolectric)
    testImplementation(libs.androidx.test.core)

    androidTestImplementation(libs.junit4)
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.androidx.test.ext.junit)
}
