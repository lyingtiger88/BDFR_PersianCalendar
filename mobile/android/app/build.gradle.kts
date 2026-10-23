plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.bdfr.zurvan"
    compileSdk = 35

    buildFeatures {
        compose = true
        buildConfig = true
    }

    val deskApiBaseUrl = providers.gradleProperty("ZURVAN_DESK_API_BASE_URL")
        .orElse("http://10.0.2.2:5088")
        .get()

    defaultConfig {
        applicationId = "com.bdfr.zurvan"
        minSdk = 26
        targetSdk = 35
        versionCode = 2
        versionName = "0.2.0-dev"
        buildConfigField(
            "String",
            "DESK_API_BASE_URL",
            "\"$deskApiBaseUrl\""
        )
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2025.01.01")
    implementation(composeBom)
    androidTestImplementation(composeBom)

    implementation("androidx.activity:activity-compose:1.10.0")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-ktx:2.8.7")
    implementation("androidx.compose.material3:material3-window-size-class")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
